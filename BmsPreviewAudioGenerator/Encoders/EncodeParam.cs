using System;
using System.Collections.Generic;
using System.Text;

namespace BmsPreviewAudioGenerator.Encoders
{
    public struct EncodeParam
    {
        public string EncodeOutputFilePath { get; set; }
        public int MixHandle { get; set; }
        public string EncodeOption { get; set; }
    }
}
