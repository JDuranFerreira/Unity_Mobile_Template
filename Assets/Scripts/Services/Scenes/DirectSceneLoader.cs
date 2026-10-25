using System;
using System.Threading;
using System.Threading.Tasks;
using Template.Core.Scenes;
using Template.Core.Services;
using UnityEngine.SceneManagement;

namespace Template.Services.Scenes
{
    /// <summary>
    /// Plain <see cref="SceneManager"/> loading behind <see cref="ISceneLoader"/>. No fades, no
    /// progress reporting yet — M3 adds those; the seam is here from M1 so the boot can end with a
    /// real scene load and nothing downstream has to change when the loader grows up.
    /// </summary>
    public sealed class DirectSceneLoader : ISceneLoader, IAppService
    {
        public string Name => "SceneLoader";

        // Nothing to warm up: SceneManager is always available. Registered anyway so the boot
        // chain contains the stage that the M3 loader will actually need.
        public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;

        public async Task LoadAsync(string sceneName, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("A scene name is required.", nameof(sceneName));
            }

            // Awaitable-style loop rather than yield: the scene loads across frames and the boot
            // must not continue until the first real scene is active.
            UnityEngine.AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                throw new InvalidOperationException(
                    $"Scene '{sceneName}' could not be loaded. Add it to Build Settings.");
            }

            while (!operation.isDone)
            {
                token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
    }
}
