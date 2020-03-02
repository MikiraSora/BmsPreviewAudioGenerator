using BmsPreviewAudioGenerator.MixEvent;
using ManagedBass;
using ManagedBass.Enc;
using ManagedBass.Mix;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace BmsPreviewAudioGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            var result = Bass.Init();

            string path = @"E:\[BMS]ENERGY SYNERGY MATRIX\";

            var content = File.ReadAllText(Path.Combine(path, "_base.bms"));
            var chart = new BMS.BMSChart(content);
            chart.Parse(BMS.ParseType.Header);
            chart.Parse(BMS.ParseType.Resources);
            chart.Parse(BMS.ParseType.Content);

            var audio_map = chart.IterateResourceData(BMS.ResourceType.wav)
                .Select(x => (x.resourceId, Directory.EnumerateFiles(path, $"{Path.GetFileNameWithoutExtension(x.dataPath)}.*").FirstOrDefault()))
                .Select(x => (x.resourceId, Bass.CreateStream(x.Item2, 0, 0, BassFlags.Decode | BassFlags.Float)))
                .ToDictionary(x => x.resourceId, x => x.Item2);

            var bms_evemts = chart.Events
                .Where(e => e.type ==
                BMS.BMSEventType.WAV
                || e.type == BMS.BMSEventType.Note
                || e.type == BMS.BMSEventType.LongNoteEnd
                || e.type == BMS.BMSEventType.LongNoteStart)
                .OrderBy(x => x.time)
                .Where(x => audio_map.ContainsKey(x.data2))
                .ToArray();

            //init mixer
            var mixer = BassMix.CreateMixerStream(44100, 2, BassFlags.Decode | BassFlags.MixerNonStop);

            //build triggers
            var mixer_events = new List<MixEventBase>(bms_evemts.Select(x => new AudioMixEvent()
            {
                Time = x.time,
                Duration = TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(audio_map[x.data2], Bass.ChannelGetLength(audio_map[x.data2]))),
                PlayOffset = TimeSpan.Zero,
                AudioHandle = audio_map[x.data2]
            }));

            //add special events to control encorder and mixer
            mixer_events.Add(new StopMixEvent { Time = mixer_events.OfType<AudioMixEvent>().Max(x => x.Duration + x.Time).Add(TimeSpan.FromSeconds(1)) });
            mixer_events.Add(new StartMixEvent() { Time = TimeSpan.FromSeconds(0) });

            int encoder = 0;

            foreach (var evt in mixer_events)
            {
                var trigger_position = Bass.ChannelSeconds2Bytes(mixer, evt.Time.TotalSeconds);

                Bass.ChannelSetSync(mixer, SyncFlags.Position | SyncFlags.Mixtime, trigger_position, (nn, mm, ss, ll) =>
                {
                    if (evt is StopMixEvent && encoder !=0 )
                    {
                        Bass.ChannelStop(mixer);
                        BassEnc.EncodeStop(encoder);
                        encoder = 0;
                    }
                    else if (evt is StartMixEvent && encoder == 0)
                    {
                        encoder = BassEnc.EncodeStart(mixer, CommandLine: "lame - output.mp3", EncodeFlags.AutoFree, null);
                    }
                    else if (evt is AudioMixEvent audio)
                    {
                        var handle = audio.AudioHandle;
                        BassMix.MixerRemoveChannel(handle);
                        Bass.ChannelSetPosition(handle, Bass.ChannelSeconds2Bytes(handle, audio.PlayOffset.TotalSeconds));
                        BassMix.MixerAddChannel(mixer, handle, BassFlags.Default);
                    }
                });
            }

            WaitChannelDataProcessed(mixer);

            Bass.Free();

            Console.WriteLine("Done!");
        }

        private static void WaitChannelDataProcessed(int handle)
        {
            var buffer = new byte[1024_000 * 10];
            int read = 0;

            do
            {
                read = Bass.ChannelGetData(handle, buffer, buffer.Length);
            } while (read > 0);
        }
    }
}
