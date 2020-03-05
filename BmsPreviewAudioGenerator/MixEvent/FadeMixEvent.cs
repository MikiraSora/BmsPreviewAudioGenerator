namespace BmsPreviewAudioGenerator.MixEvent
{
    public class FadeMixEvent : MixEventBase
    {
        public FadeMixEvent(bool fade_out, int duration)
        {
            FadeOut = fade_out;
            Duration = duration;
        }

        public bool FadeOut { get; }
        public int Duration { get; }
    }
}
