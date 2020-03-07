using ManagedBass;
using System;
using System.Collections.Generic;
using System.Text;

namespace BmsPreviewAudioGenerator
{
    public class VolumeEffect : Effect<VolumeParameters>
    {
        public float Target
        {
            get => Parameters.fTarget;
            set
            {
                Parameters.fTarget = value;
                OnPropertyChanged();
            }
        }

        public float Current
        {
            get => Parameters.fCurrent;
            set
            {
                Parameters.fCurrent = value;
                OnPropertyChanged();
            }
        }

        public float Time
        {
            get => Parameters.fTime;
            set
            {
                Parameters.fTime = value;
                OnPropertyChanged();
            }
        }

        public uint Curve
        {
            get => Parameters.lCurve;
            set
            {
                Parameters.lCurve = value;
                OnPropertyChanged();
            }
        }
    }
}
