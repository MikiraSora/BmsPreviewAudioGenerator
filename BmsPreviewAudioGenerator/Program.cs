using ManagedBass;
using ManagedBass.Mix;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ConsoleApp22
{
    class Program
    {
        static BMS.BMSChart chart;

        static void Main(string[] args)
        {
            var result = Bass.Init();

            string path = @"E:\[BMS]ENERGY SYNERGY MATRIX\";

            var content = File.ReadAllText(Path.Combine(path, "_base.bms"));
            chart = new BMS.BMSChart(content);
            chart.Parse(BMS.ParseType.Header);

            Console.WriteLine(chart.Title);
            Console.WriteLine(chart.Artist);

            chart.Parse(BMS.ParseType.Resources);

            chart.Parse(BMS.ParseType.Content);

            var audio_map = chart.IterateResourceData(BMS.ResourceType.wav)
                .Select(x => (x.resourceId, Directory.EnumerateFiles(path, $"{Path.GetFileNameWithoutExtension(x.dataPath)}.*").FirstOrDefault()))
                .Select(x => (x.resourceId, LoadAudio(x.Item2)))
                .ToDictionary(x => x.resourceId, x => x.Item2);

            var events = chart.Events
                .Where(e => e.type ==
                BMS.BMSEventType.WAV
                || e.type == BMS.BMSEventType.Note
                || e.type == BMS.BMSEventType.LongNoteEnd
                || e.type == BMS.BMSEventType.LongNoteStart)
                .OrderBy(x => x.time)
                .ToArray();

            var prev_time = 0d;
            Stopwatch sw = new Stopwatch();

            foreach (var evt in events)
            {
                var time = evt.time.TotalMilliseconds;
                sw.Restart();
                while (prev_time + sw.ElapsedMilliseconds < time);
                prev_time = time;

                Bass.ChannelPlay(audio_map[evt.data2],true);
            }
            
            Console.ReadLine();
        }

        private static int LoadAudio(string item2)
        {
            return Bass.CreateStream(item2, 0, 0, BassFlags.Default);
        }
    }
}
