using ManagedBass;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BmsPreviewAudioGenerator
{
    [StructLayout(LayoutKind.Sequential)]
    public class VolumeParameters : IEffectParameter
    {
        public float fTarget;
        public float fCurrent;
        public float fTime;
        public uint lCurve;

        public EffectType FXType => /*EffectType.Volume*/(EffectType)9;
    }
}
