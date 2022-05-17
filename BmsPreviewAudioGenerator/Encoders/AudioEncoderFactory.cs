using BmsPreviewAudioGenerator.Encoders.Implements;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BmsPreviewAudioGenerator.Encoders
{
    public static class AudioEncoderFactory
    {
        public static IAudioEncoder CreateAudioEncoder(string saveAudioFileName)
        {
            var extFileName = Path.GetExtension(saveAudioFileName).Trim().TrimStart('.');

            return extFileName switch
            {
                "mp3" => CreateAudioEncoder(SupportEncodingType.Mp3),
                _ => CreateAudioEncoder(SupportEncodingType.Ogg),
            };
        }

        public static IAudioEncoder CreateAudioEncoder(SupportEncodingType encodingType)
            => encodingType switch
            {
                SupportEncodingType.Opus => new OpusAudioEncoder(),
                SupportEncodingType.Mp3 => new Mp3AudioEncoder(),
                 _ => new OggAudioEncoder(),
            };
    }
}
