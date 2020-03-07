using BMS;
using BmsPreviewAudioGenerator.MixEvent;
using ManagedBass;
using ManagedBass.Enc;
using ManagedBass.Fx;
using ManagedBass.Mix;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BmsPreviewAudioGenerator
{
    class Program
    {
        private static int ProcessBufferSize { get; set; }

        private static string[] support_bms_format = new[]
        {
            ".bms"
        };

        static void Main(string[] args)
        {
            Console.WriteLine($"Program version:{typeof(Program).Assembly.GetName().Version}");

            if (!Bass.Init())
            {
                Console.WriteLine($"Init BASS failed:{Bass.LastError}");
                return;
            }

            Console.WriteLine($"Init BASS successfully.");

            ProcessBufferSize = CommandLine.TryGetOptionValue<int>("process_buffer_size", out var q) ? q : 20_000;

            var st = CommandLine.TryGetOptionValue<string>("start", out var s) ? s : null;
            var et = CommandLine.TryGetOptionValue<string>("end", out var e) ? e : null;
            var fo = CommandLine.TryGetOptionValue<int>("fade_out", out var foo) ? foo : 0;
            var fi = CommandLine.TryGetOptionValue<int>("fade_in", out var fii) ? fii : 0;
            var sn = CommandLine.TryGetOptionValue<string>("save_name", out var sw) ? sw : "preview_auto_generator.ogg";
            var path = CommandLine.TryGetOptionValue<string>("path", out var p) ? p : throw new Exception("MUST type a path.");
            var bms = CommandLine.TryGetOptionValue<string>("bms", out var b) ? b : null;
            var batch = CommandLine.ContainSwitchOption("batch");
            var fc = CommandLine.ContainSwitchOption("fast_clip");
            var cv = CommandLine.ContainSwitchOption("check_valid");
            var ns = CommandLine.ContainSwitchOption("no_skip");
            var rm = CommandLine.ContainSwitchOption("rm");

            if (CommandLine.ContainSwitchOption("support_extend_format"))
            {
                support_bms_format = new[]
                {
                    ".bms",
                    ".bme",
                    ".bml",
                    ".bmson",
                };
            }

            if (rm)
            {
                DeleteGeneratedAudioFiles(path, sn);
                return;
            }

            if (batch && !string.IsNullOrWhiteSpace(bms))
                throw new Exception("Not allow set param \"bms\" and \"batch\" at same time!");

            var target_directories = batch ? EnumerateConvertableDirectories(path) : new[] { path };

            HashSet<string> failed_paths = new HashSet<string>();

            for (int i = 0; i < target_directories.Length; i++)
            {
                Console.WriteLine($"-------\t{i + 1}/{target_directories.Length} ({100.0f * (i + 1) / target_directories.Length:F2}%)\t-------");
                var dir = target_directories[i];
                try
                {
                    if (!GeneratePreviewAudio(dir, bms, st, et, save_file_name: sn, no_skip:ns, fast_clip: fc,check_vaild: cv, fade_in: fi, fade_out: fo))
                        failed_paths.Add(dir);
                }
                catch (Exception ex)
                {
                    failed_paths.Add(dir);
                    Console.WriteLine($"Failed.\n{ex.Message}\n{ex.StackTrace}");
                }

                if (Bass.LastError != Errors.OK)
                {
                    Console.WriteLine($"Bass get error:{Bass.LastError},try reinit...");
                    Bass.Free();
                    if (!Bass.Init())
                    {
                        Console.WriteLine($"Reinit BASS failed:{Bass.LastError}");
                        return;
                    }
                    Console.WriteLine($"Success reinit BASS.");
                }
            }

            Console.WriteLine($"\n\n\nGenerate failed list({failed_paths.Count}):");
            foreach (var fp in failed_paths)
                Console.WriteLine(fp);

            Bass.Free();
        }

        private static void DeleteGeneratedAudioFiles(string path, string sn)
        {
            if (string.IsNullOrWhiteSpace(sn))
                throw new Exception("Must set param \"save_name\"");

            //enumerate files and safe check
            var delete_targets = Directory.EnumerateFiles(path, sn, SearchOption.AllDirectories)
                .Where(x =>
                {
                    x = Path.GetFileName(x);
                    return x.StartsWith("preview", StringComparison.InvariantCultureIgnoreCase) && support_extension_names.Any(y => x.EndsWith(y, StringComparison.InvariantCultureIgnoreCase));
                });

            int s = 0, f = 0;

            foreach (var file_path in delete_targets)
            {
                try
                {
                    File.Delete(file_path);
                    s++;
                    Console.WriteLine($"Deleted successfully {file_path} ");
                }
                catch (Exception e)
                {
                    f++;
                    Console.WriteLine($"Deleted failed {file_path} : {e.Message}");
                }
            }

            Console.WriteLine($"Enumerated {s + f} files , success:{s} failed:{f}");
        }

        private static string[] EnumerateConvertableDirectories(string path)
        {
            var result = Directory.EnumerateFiles(path, "*.bm*", SearchOption.AllDirectories).Where(x => support_bms_format.Any(y => x.EndsWith(y, StringComparison.InvariantCultureIgnoreCase))).Select(x => Path.GetDirectoryName(x)).Distinct().ToArray();

            return result;
        }

        /// <summary>
        /// 切鸡鸡
        /// </summary>
        /// <param name="dir_path"></param>
        /// <param name="specific_bms_file_name">可选,钦定文件夹下某个bms谱面文件，如果不钦定就随机选取一个</param>
        /// <param name="start_time">起始时间，单位毫秒或者百分比，默认最初</param>
        /// <param name="end_time">终止时间，单位毫秒或者百分比，默认谱面末尾</param>
        /// <param name="encoder_command_line">编码命令</param>
        /// <param name="save_file_name">保存的文件名</param>
        public static bool GeneratePreviewAudio(
            string dir_path,
            string specific_bms_file_name = null,
            string start_time = null,
            string end_time = null,
            string encoder_command_line = "",
            string save_file_name = "preview_auto_generator.ogg",
            int fade_out = 0,
            int fade_in = 0,
            bool check_vaild = false,
            bool fast_clip = false,
            bool no_skip = false)
        {
            var created_audio_handles = new HashSet<int>();
            var sync_record = new HashSet<int>();
            int mixer = 0;

            try
            {
                save_file_name = string.IsNullOrWhiteSpace(save_file_name) ? "preview_auto_generator.ogg" : save_file_name;

                if (!Directory.Exists(dir_path))
                    throw new Exception($"Directory {dir_path} not found.");

                var bms_file_path = string.IsNullOrWhiteSpace(specific_bms_file_name) ? Directory.EnumerateFiles(dir_path, "*.bm*", SearchOption.TopDirectoryOnly).Where(x => support_bms_format.Any(y => x.EndsWith(y, StringComparison.InvariantCultureIgnoreCase))).FirstOrDefault() : Path.Combine(dir_path, specific_bms_file_name);

                if (!File.Exists(bms_file_path))
                    throw new Exception($"BMS file {bms_file_path} not found.");

                Console.WriteLine($"BMS file path:{bms_file_path}");

                var content = File.ReadAllText(bms_file_path);

                if (((check_vaild && CheckBeforeFileVaild(dir_path, save_file_name)) || CheckSkipable(dir_path, content))&&!no_skip)
                {
                    Console.WriteLine("This bms contains preview audio file, skiped.");
                    return true;
                }

                var chart = bms_file_path.EndsWith(".bmson", StringComparison.InvariantCultureIgnoreCase) ? new BmsonChart(content) as Chart : new BMSChart(content);
                chart.Parse(ParseType.Header);
                chart.Parse(ParseType.Resources);
                chart.Parse(ParseType.Content);


                var audio_map = chart.IterateResourceData(ResourceType.wav)
                    .Select(x => (x.resourceId, Directory.EnumerateFiles(dir_path, $"{Path.GetFileNameWithoutExtension(x.dataPath)}.*").FirstOrDefault()))
                    .Select(x => (x.resourceId, LoadAudio(x.Item2)))
                    .ToDictionary(x => x.resourceId, x => x.Item2);

                var bms_evemts = chart.Events
                    .Where(e => e.type ==
                    BMSEventType.WAV
                    || e.type == BMSEventType.Note
                    || e.type == BMSEventType.LongNoteEnd
                    || e.type == BMSEventType.LongNoteStart)
                    .OrderBy(x => x.time)
                    .Where(x => audio_map.ContainsKey(x.data2))//filter
                    .ToArray();

                //init mixer
                mixer = BassMix.CreateMixerStream(44100, 2, BassFlags.Decode | BassFlags.MixerNonStop);

                //build triggers
                var mixer_events = new List<MixEventBase>(bms_evemts.Select(x => new AudioMixEvent()
                {
                    Time = x.time,
                    Duration = TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(audio_map[x.data2], Bass.ChannelGetLength(audio_map[x.data2]))),
                    PlayOffset = TimeSpan.Zero,
                    AudioHandle = audio_map[x.data2]
                }));

                #region Calculate and Adjust StartTime/EndTime

                var full_audio_duration = mixer_events.OfType<AudioMixEvent>().Max(x => x.Duration + x.Time).Add(TimeSpan.FromSeconds(1));
                var actual_end_time = string.IsNullOrWhiteSpace(end_time) ? full_audio_duration : (end_time.EndsWith("%") ? TimeSpan.FromMilliseconds(float.Parse(end_time.TrimEnd('%')) / 100.0f * full_audio_duration.TotalMilliseconds) : TimeSpan.FromMilliseconds(int.Parse(end_time)));
                var actual_start_time = string.IsNullOrWhiteSpace(start_time) ? TimeSpan.Zero : (start_time.EndsWith("%") ? TimeSpan.FromMilliseconds(float.Parse(start_time.TrimEnd('%')) / 100.0f * full_audio_duration.TotalMilliseconds) : TimeSpan.FromMilliseconds(int.Parse(start_time)));

                actual_start_time = actual_start_time < TimeSpan.Zero ? TimeSpan.Zero : actual_start_time;
                actual_start_time = actual_start_time > full_audio_duration ? full_audio_duration : actual_start_time;

                actual_end_time = actual_end_time < TimeSpan.Zero ? TimeSpan.Zero : actual_end_time;
                actual_end_time = actual_end_time > full_audio_duration ? full_audio_duration : actual_end_time;

                if (actual_end_time < actual_start_time)
                {
                    var t = actual_end_time;
                    actual_end_time = actual_start_time;
                    actual_start_time = t;
                }

                Console.WriteLine($"Actual clip({(int)full_audio_duration.TotalMilliseconds}ms):{(int)actual_start_time.TotalMilliseconds}ms ~ {(int)actual_end_time.TotalMilliseconds}ms");

                #endregion

                if (fast_clip)
                    FastClipEvent(mixer_events, ref actual_start_time, ref actual_end_time);

                //add special events to control encorder and mixer
                mixer_events.Add(new StopMixEvent { Time = actual_end_time });
                mixer_events.Add(new StartMixEvent() { Time = actual_start_time });

                //save encoder handle
                int encoder = 0;

                #region apply fade in/out

                var effect = new VolumeParameters();
                var fx = Bass.ChannelSetFX(mixer, effect.FXType, 0);

                if (fade_in!=0)
                {
                    var fade_in_evt = new FadeMixEvent(false, fade_in)
                    {
                        Time = actual_start_time
                    };

                    mixer_events.Add(fade_in_evt);
                }

                if (fade_out != 0)
                {
                    var fade_out_evt = new FadeMixEvent(true, fade_out)
                    {
                        Time = actual_end_time.Subtract(TimeSpan.FromMilliseconds(fade_out))
                    };

                    mixer_events.Add(fade_out_evt);
                }

                #endregion

                foreach (var evt in mixer_events)
                {
                    var trigger_position = Bass.ChannelSeconds2Bytes(mixer, evt.Time.TotalSeconds);

                    sync_record.Add(Bass.ChannelSetSync(mixer, SyncFlags.Position | SyncFlags.Mixtime, trigger_position, (nn, mm, ss, ll) =>
                    {
                        if (evt is StopMixEvent && encoder != 0)
                        {
                            Bass.ChannelStop(mixer);
                            BassEnc.EncodeStop(encoder);
                            encoder = 0;
                        }
                        else if (evt is StartMixEvent && encoder == 0)
                        {
                            var output_path = Path.Combine(dir_path, save_file_name);
                            Console.WriteLine($"Encoding output file path:{output_path}");
                            encoder = BassEnc_Ogg.Start(mixer, encoder_command_line, EncodeFlags.AutoFree, output_path);
                        }
                        else if (evt is AudioMixEvent audio)
                        {
                            var handle = audio.AudioHandle;
                            BassMix.MixerRemoveChannel(handle);
                            Bass.ChannelSetPosition(handle, Bass.ChannelSeconds2Bytes(handle, audio.PlayOffset.TotalSeconds));
                            BassMix.MixerAddChannel(mixer, handle, BassFlags.Default);
                        }
                        else if (evt is FadeMixEvent fade)
                        {
                            effect.fTime = fade.Duration / 1000.0f;

                            if (fade.FadeOut)
                            {
                                effect.fCurrent = 1;
                                effect.fTarget = 0;
                            }
                            else
                            {
                                effect.fCurrent = 0;
                                effect.fTarget = 1;
                            }

                            Bass.FXSetParameters(fx, effect);
                        }
                    }));
                }

                WaitChannelDataProcessed(mixer);

                Console.WriteLine("Success!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed.\n{ex.Message}\n{ex.StackTrace}");
                return false;
            }
            finally
            {
                #region Clean Resource

                foreach (var record in sync_record)
                    Bass.ChannelRemoveSync(mixer, record);

                foreach (var handle in created_audio_handles)
                    Bass.StreamFree(handle);

                if (mixer != 0)
                    Bass.StreamFree(mixer);

                #endregion
            }

            int LoadAudio(string item2)
            {
                var handle = Bass.CreateStream(item2, 0, 0, BassFlags.Decode | BassFlags.Float);

                created_audio_handles.Add(handle);

                return handle;
            }
        }

        /// <summary>
        /// 检查已生成的文件是否存在,或为空文件
        /// </summary>
        /// <param name="dir_path"></param>
        /// <param name="save_file_name"></param>
        /// <returns></returns>
        private static bool CheckBeforeFileVaild(string dir_path, string save_file_name)
        {
            var path = Path.Combine(dir_path, save_file_name);

            if ((!File.Exists(path)))
                return false;

            using var fs = File.OpenRead(path);

            return fs.Length != 0;
        }

        private static void FastClipEvent(List<MixEventBase> mixer_events, ref TimeSpan actual_start_time, ref TimeSpan actual_end_time)
        {
            //remove events which out of range and never play
            var tst = actual_start_time;
            var tet = actual_end_time;
            var remove_count = mixer_events.RemoveAll(e =>
            e is AudioMixEvent evt && (((evt.Time.Add(evt.Duration)) < tst) || (evt.Time > tet)));

            foreach (var evt in mixer_events.OfType<AudioMixEvent>().Where(x => x.Time < tst))
                evt.PlayOffset = tst - evt.Time;

            foreach (var evt in mixer_events)
                evt.Time -= evt is AudioMixEvent audio_evt ? (tst - audio_evt.PlayOffset) : tst;

            actual_start_time -= tst;
            actual_end_time -= tst;

            Console.WriteLine($"Fast clip:remove {remove_count} events,now is {(int)actual_start_time.TotalMilliseconds}ms ~ {(int)actual_end_time.TotalMilliseconds}ms");
        }

        private readonly static string[] support_extension_names = new[]
        {
            ".ogg",".mp3",".wav"
        };

        private static bool CheckSkipable(string dir_path, string content)
        {
            //check if there exist file named "preview*.(ogg|mp3|wav)"
            if (Directory.EnumerateFiles(dir_path, "preview*").Any(x => support_extension_names.Any(y => x.EndsWith(y, StringComparison.InvariantCultureIgnoreCase))))
                return true;

            if (content.Contains("#preview", StringComparison.InvariantCultureIgnoreCase))
                return true;

            return false;
        }

        private static void WaitChannelDataProcessed(int handle)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(ProcessBufferSize);

            while (true)
            {
                if (Bass.ChannelGetData(handle, buffer, buffer.Length) <= 0)
                    break;
            }

            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
