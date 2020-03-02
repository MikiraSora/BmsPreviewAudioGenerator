using System;
using System.Collections.Generic;
using System.Text;

namespace BmsPreviewAudioGenerator.MixEvent
{
    public class AudioMixEvent:MixEventBase
    {
        public int AudioHandle { get; set; }
        public TimeSpan Duration { get; set; }
        public TimeSpan PlayOffset { get; set; } = TimeSpan.Zero;
    }
}
