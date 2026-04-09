using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Defines a single input or output port on a building in its unrotated, unflipped local space.
    /// localCell is relative to the anchor (bottom-left corner of the footprint).
    /// localFacing is the edge of that cell the port is on (i.e. the direction a conveyor would connect from).
    /// </summary>
    [Serializable]
    public struct BuildingPort
    {
        public PortType       portType;
        public Vector2Int     localCell;    // (0,0) = anchor cell; (1,0) = one cell to the right, etc.
        public OutputDirection localFacing; // which face of the cell the port sits on
    }
}
