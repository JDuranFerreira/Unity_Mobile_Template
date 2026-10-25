using System.Threading;
using System.Threading.Tasks;

namespace Template.Core.Scenes
{
    /// <summary>
    /// The seam every scene transition goes through. Direct scene loading behind this interface
    /// now; an Addressables-backed implementation can replace it later without touching callers
    /// (architecture note, Decisions table).
    /// </summary>
    public interface ISceneLoader
    {
        Task LoadAsync(string sceneName, CancellationToken token = default);
    }
}
