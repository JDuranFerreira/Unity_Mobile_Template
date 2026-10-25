using System;
using System.Threading;
using System.Threading.Tasks;
using Template.Core.Boot;
using Template.Core.Events;
using Template.Core.Scenes;
using Template.Core.Services;
using Template.Services.Events;
using Template.Services.Scenes;
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
        /// The canonical init order (architecture note, M1): save → settings → audio → localization
        /// → analytics → ads → purchases → UI. Each milestone plugs its services in at its own
        /// stage, in this method, in this order. Registration order *is* boot order, so this list is
        /// the contract — reordering lines reorders initialization.
        ///
        /// M1 has one service (the scene loader, which runs last, after UI in the full list).
        /// </summary>
        static void RegisterServices(ServiceRegistry registry)
        {
            registry.Register(new DirectSceneLoader());
        }
    }
}
