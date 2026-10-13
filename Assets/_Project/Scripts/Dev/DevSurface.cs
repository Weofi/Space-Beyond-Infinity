using UnityEngine;
using Voidward.Core;

namespace Voidward.Dev
{
    /// <summary>Test wall in the dev "Lab" scenario: tells impacts / decals which surface it is.</summary>
    public class DevSurface : MonoBehaviour, ISurface
    {
        public SurfaceKind Kind = SurfaceKind.Metal;
        public SurfaceKind Surface => Kind;
    }
}
