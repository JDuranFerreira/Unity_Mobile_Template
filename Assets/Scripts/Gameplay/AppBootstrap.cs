using System;
using System.Threading;
using System.Threading.Tasks;
using Template.Core.Boot;
using Template.Core.Events;
using Template.Core.Save;
using Template.Core.Scenes;
using Template.Core.Services;
using Template.Core.Settings;
using Template.Services.Audio;
using Template.Services.Events;
using Template.Services.Save;
using Template.Services.Scenes;
using Template.Services.Settings;
using Template.Services.UI;
using UnityEngine;

namespace Template.Gameplay
{
    /// <summary>
    /// The composition root (architecture note, Decisions table: "Bootstrap constructs, everything
    /// else receives"). It owns the registry and the bus, constructs every service, and runs the
    /// boot chain. Nothing else in the template news up a service.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class AppBootstrap : MonoBehaviour
    {
        /// <summary>First real scene loaded once boot succeeds. Change it per game.</summary>
        public const string DefaultFirstScene = "MainMenu";

        [SerializeField] string firstSceneName = DefaultFirstScene;

        /// <summary>Keys-to-clips for the audio host. Null is legal: audio then logs misses and plays nothing.</summary>
        [SerializeField] AudioLibrary audioLibrary;

        public static AppBootstrap Instance { get; private set; }

        public ServiceRegistry Services { get; private set; }

        public IEventBus Bus { get; private set; }

        public BootResult Result { get; private set; }

        public bool IsBooted { get; private set; }

        /// <summary>
        /// Ensures services exist when Play is pressed from any scene, not only Bootstrap. Runs
        /// before the first scene is loaded, so the scene's own objects wake up with the bus and
        /// registry already in place.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void EnsureBootstrapExists()
        {
            if (Instance != null)
            {
                return;
            }

            GameObject host = new GameObject(nameof(AppBootstrap));
            DontDestroyOnLoad(host);
            host.AddComponent<AppBootstrap>();
        }

        void Awake()
        {
            // A scene that ships its own AppBootstrap loses to the one created above; the survivor
            // must be the single composition root or two registries would fight over the services.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            Bus = new EventBus();
            Services = new ServiceRegistry();
            RegisterServices(Services);
        }

        void Start()
        {
            // Fire and forget by design: Awake/Start cannot await, and a failed boot is reported on
            // the bus (AppBootFailed) and through Result rather than thrown into the player loop.
            _ = BootAsync();
        }

        public async Task<BootResult> BootAsync(CancellationToken token = default)
        {
            if (IsBooted)
            {
                return Result;
            }

            AppBoot boot = new AppBoot(Services, Bus);
            Result = await boot.RunAsync(token);
            IsBooted = Result.Succeeded;

            if (!Result.Succeeded)
            {
                if (Result.WasCancelled)
                {
                    Debug.LogWarning("[AppBootstrap] Boot cancelled.", this);
                }
                else
                {
                    Debug.LogError(
                        $"[AppBootstrap] Boot failed in '{Result.FailedService}': {Result.Error?.Message}",
                        this);
                }

                return Result;
            }

            ISceneLoader scenes = Services.Get<ISceneLoader>();
            await scenes.LoadAsync(firstSceneName, token);
            return Result;
        }

        /// <summary>
        /// Mobile durability: pause and quit are where Unity warns that the process may vanish, so
        /// pending save state is flushed here (architecture note: save on OnApplicationPause /
        /// OnApplicationQuitting). The game itself saves on its own gameplay events; this hook only
        /// guarantees that whatever is queued up becomes durable before the process dies.
        /// </summary>
        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                FlushSave();
            }
        }

        void OnApplicationQuit()
        {
            FlushSave();
        }

        void FlushSave()
        {
            if (Services == null || !Services.TryGet<ISaveService>(out ISaveService save))
            {
                return;
            }

            // Writes are synchronous today, so the flush is already durable when SaveAsync
            // returned; the fire-and-forget keeps that contract for a future queueing store.
            _ = save.FlushAsync();
        }

        /// <summary>
        /// M3 chain: save, settings, audio, UI, scene loader. The ProjectSettings notes: save comes
        /// first because every other service may load defaults, the UI manager hands the
        /// (DontDestroyOnLoad) overlay its transition cover as a Component, so the loader
        /// reporting covered transitions comes last.
        /// </summary>
        void RegisterServices(ServiceRegistry registry)
        {
            registry.Register(new SaveService(new FileSaveStore(), Bus));

            registry.Register(new SettingsService(Bus, new PlayerPrefsStore()));

            AudioHost audio = gameObject.AddComponent<AudioHost>();
            audio.Library = audioLibrary;
            audio.Construct(Bus, Services.Get<ISettingsService>());
            registry.Register(audio);

            UIManager ui = new UIManager();
            registry.Register(ui);

            GameObject overlayHost = new GameObject("SceneTransitionOverlay");
            DontDestroyOnLoad(overlayHost);
            SceneFadeOverlay overlay = overlayHost.AddComponent<SceneFadeOverlay>();

            registry.Register(new TransitionSceneLoader(Bus, overlay, UnloadUnusedAssetsAsync));
        }

        /// <summary>
        /// The loader's after-transition hook (M3 plan: UnloadUnusedAssets after each covered
        /// load, so twenty transitions do not creep memory with assets the old scene dropped).
        /// Driven by Awaitable instead of a blocking read of isDone.
        /// </summary>
        static async Task UnloadUnusedAssetsAsync()
        {
            AsyncOperation unload = Resources.UnloadUnusedAssets();
            while (!unload.isDone)
            {
                await Awaitable.NextFrameAsync();
            }
        }
    }
}
