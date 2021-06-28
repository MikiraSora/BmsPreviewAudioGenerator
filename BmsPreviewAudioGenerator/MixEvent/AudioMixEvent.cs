using System;

namespace BmsPreviewAudioGenerator.MixEvent
{
    public class AudioMixEvent : MixEventBase
    {
        public int AudioHandle { get; set; }
        public int WavId { get; set; }
        public TimeSpan Duration { get; set; }
        public TimeSpan PlayOffset { get; set; } = TimeSpan.Zero;
    }
}
