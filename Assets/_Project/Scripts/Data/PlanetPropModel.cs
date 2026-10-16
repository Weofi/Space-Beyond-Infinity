using System;
using UnityEngine;

namespace Voidward.Data
{
    /// <summary>
    /// One planet prop / asteroid asset (#100, Art/Models/Planet, normalized by Tools/Blender/planet_assets.py):
    /// rocks have the origin at the bounds centre and radius 1, everything else the origin at the bottom centre and
    /// height 1, so the game scales them by one number. The id names the family ("Rock03", "Crystal01", "AlienTree").
    /// The mesh is CPU-readable: PlanetDecor copies it into the chunk meshes on worker threads.
    /// </summary>
    [Serializable]
    public class PlanetPropModel
    {
        public string Id;
        public Mesh Mesh;
        public Material[] Materials;
    }
}
