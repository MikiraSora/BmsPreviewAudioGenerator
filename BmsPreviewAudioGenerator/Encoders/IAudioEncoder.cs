using System;
using System.Collections.Generic;
using System.Text;

namespace BmsPreviewAudioGenerator.Encoders
{
    public interface IAudioEncoder
    {
        bool IsEncoding { get; }
        void BeginEncode(EncodeParam param);
        void EndEncode();
    }
}
