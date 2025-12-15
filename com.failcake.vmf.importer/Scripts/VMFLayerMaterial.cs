using System;

namespace FailCake.VMF
{
    
    [Serializable]
    public enum VMFLayer : byte
    {
        LAYER_0 = 0,
        LAYER_1,
        LAYER_2,
        LAYER_3,
        
        COUNT
    }

    public class VMFLayerMaterial : VMFBoxMaterial
    {
        public VMFLayer layer;
    }
}