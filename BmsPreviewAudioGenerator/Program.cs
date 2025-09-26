using BmsPreviewAudioGenerator.Encoders;
using BmsPreviewAudioGenerator.MixEvent;
using CSBMSParser;
using ManagedBass;
using ManagedBass.Enc;
using ManagedBass.Fx;
using ManagedBass.Mix;
using ManagedBass.Opus;
using System;
using System.Collections.Concurrent;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ude;

namespace BmsPreviewAudioGenerator
{
    class Program
    {
        private static string cs = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        public static string NumberToString(long num)
        {
            string str = string.Empty;
            while (num >= 36)
            {
                str = cs[(int)(num % 36)] + str;
                num = num / 36;
            }
            return cs[(int)num] + str;
        }

        private static int ProcessBufferSize { get; set; }

        private static string[] support_bms_format = new[]
        {
            ".bms"
        };

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Console.WriteLine($"Program version:{typeof(Program).Assembly.GetName().Version}");

            if (args.Length == 0)
            {
                Console.WriteLine($"Program Usage : https://github.com/MikiraSora/BmsPreviewAudioGenerator");
                return;
            }

            if (!Bass.Init())
            {
                Console.WriteLine($"Init BASS failed:{Bass.LastError}");
                return;
            }

            Console.WriteLine($"Init BASS successfully.");

            ProcessBufferSize = CommandLine.TryGetOptionValue<int>("process_buffer_size", out var q) ? q : 20_000;

            var th = CommandLine.TryGetOptionValue<int>("thread", out var thh) ? Math.Min(Math.Max(1, thh), Environment.ProcessorCount) : 1;
            var st = CommandLine.TryGetOptionValue<string>("start", out var s) ? s : null;
            var et = CommandLine.TryGetOptionValue<string>("end", out var e) ? e : null;
            var fo = CommandLine.TryGetOptionValue<int>("fade_out", out var foo) ? foo : 0;
            var fi = CommandLine.TryGetOptionValue<int>("fade_in", out var fii) ? fii : 0;
            var enc = CommandLine.TryGetOptionValue<SupportEncodingType>("encoder", out var encoding) ? encoding : SupportEncodingType.Any;
            var sn = CommandLine.TryGetOptionValue<string>("save_name", out var sw) ? sw : "preview_auto_generator.ogg";
            var eopt = CommandLine.TryGetOptionValue<string>("encoder_option_base64", out var eob) ? Encoding.UTF8.GetString(Convert.FromBase64String(eob)) : "";
            var path = CommandLine.TryGetOptionValue<string>("path", out var p) ? p : throw new Exception("MUST type a path.");
            var bms = CommandLine.TryGetOptionValue<string>("bms", out var b) ? b : null;
            var batch = CommandLine.ContainSwitchOption("batch");
            var fc = CommandLine.ContainSwitchOption("fast_clip");
            var cv = CommandLine.ContainSwitchOption("check_valid");
            var ns = CommandLine.ContainSwitchOption("no_skip");
            var cam = CommandLine.ContainSwitchOption("check_audio_missing");
            var rm = CommandLine.ContainSwitchOption("rm");

            if (th > 1)
            {
                Console.WriteLine($"program will use parallel, parallel size: {th}");
                Bass.Configure(Configuration.UpdateThreads, th);
            }

            if (CommandLine.ContainSwitchOption("support_extend_format"))
            {
                support_bms_format = new[]
                {
                    ".bms",
                    ".bme",
                    ".bml",
                    ".pms",
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

            var failed_paths = new ConcurrentBag<(string path, string reason)>();
            var consoleLogLocker = new object();
            var i = 0;

            var start_time = DateTime.Now;

            Parallel.For(0, target_directories.Length, new ParallelOptions()
            {
                MaxDegreeOfParallelism = th
            }, current_task_index =>
            {
                ThreadLocalLogger.Instance.Clear();
                var dir = target_directories[current_task_index];
                try
                {
                    if (!GeneratePreviewAudio(dir, bms, st, et,
                        save_file_name: sn,
                        no_skip: ns,
                        fast_clip: fc,
                        check_vaild: cv,
                        fade_in: fi,
                        fade_out: fo,
                        ignore_audio_missing: !cam,
                        encoding_type: enc,
                        encoder_command_line: eopt))
                    {
                        lock (failed_paths)
                        {
                            failed_paths.Add((dir, default));
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (failed_paths)
                    {
                        failed_paths.Add((dir, ex.Message));
                    }
                    ThreadLocalLogger.Instance.Log($"Failed.\n{ex.Message}\n{ex.StackTrace}");
                }

                if (Bass.LastError != Errors.OK)
                    ThreadLocalLogger.Instance.Log($"Bass get error:{Bass.LastError}...");

                lock (consoleLogLocker)
                {
                    var completeIdx = (i++) + 1;
                    Console.WriteLine();
                    Console.WriteLine($"-------\t{completeIdx}/{target_directories.Length} ({100.0f * completeIdx / target_directories.Length:F2}%)\t-------");
                    Console.Write(ThreadLocalLogger.Instance.ToString());
                    Console.WriteLine($"-----------------------");
                    Console.WriteLine();
                }
            });

            Console.WriteLine($"\n\n\nGenerate failed list({failed_paths.Count}):");
            foreach ((var failedFilePath, var reason) in failed_paths)
                Console.WriteLine($"{failedFilePath}{(reason != null ? $"\t({reason})" : string.Empty)}");

            Console.WriteLine($"Spent time: {DateTime.Now - start_time}");

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
            var result = Directory.EnumerateFiles(path, "*.*m*", SearchOption.AllDirectories).Where(x => support_bms_format.Any(y => x.EndsWith(y, StringComparison.InvariantCultureIgnoreCase))).Select(x => Path.GetDirectoryName(x)).Distinct().ToArray();

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
            bool no_skip = false,
            SupportEncodingType encoding_type = SupportEncodingType.Any,
            bool ignore_audio_missing = false)
        {
            var created_audio_handles = new HashSet<int>();
            var sync_record = new HashSet<int>();
            int mixer = 0;

            try
            {
                save_file_name = string.IsNullOrWhiteSpace(save_file_name) ? "preview_auto_generator.ogg" : save_file_name;

                if (!Directory.Exists(dir_path))
                    throw new Exception($"Directory {dir_path} not found.");

                var bms_file_path = string.IsNullOrWhiteSpace(specific_bms_file_name) ? Directory.EnumerateFiles(dir_path, "*.*m*", SearchOption.TopDirectoryOnly).Where(x => support_bms_format.Any(y => x.EndsWith(y, StringComparison.InvariantCultureIgnoreCase))).FirstOrDefault() : Path.Combine(dir_path, specific_bms_file_name);

                if (!File.Exists(bms_file_path))
                    throw new Exception($"BMS file {bms_file_path} not found.");

                ThreadLocalLogger.Instance.Log($"BMS file path:{bms_file_path}");

                var encoding = DetectEncoding(bms_file_path);
                var content = File.ReadAllText(bms_file_path, encoding);

                if (((check_vaild && CheckBeforeFileVaild(dir_path, save_file_name)) || CheckSkipable(dir_path, content)) && !no_skip)
                {
                    ThreadLocalLogger.Instance.Log($"This bms contains preview audio file, skiped.");
                    return true;
                }

                string searchAudioFile(string audioPathName)
                {
                    //比如会遇到 “key.ogg”
                    //比如会遇到 “ogg/key.ogg”
                    //比如会遇到 “ogg/key.mp3”但只有key.wav
                    //比如会遇到 “ogg/key”
                    //甚至还会遇到 “key.ogg”但在sound子文件夹

                    var path = dir_path;
                    var split = audioPathName.Split('/');

                    if (split.Length > 1)
                    {
                        audioPathName = split.LastOrDefault();
                        for (int i = 0; i < split.Length - 1; i++)
                            path = Path.Combine(path, split[i]);
                    }

                    var actualSearchPattern = $"{Path.GetFileNameWithoutExtension(audioPathName)}.*";

                    bool checkExt(string filePath)
                    {
                        var ext = Path.GetExtension(filePath);
                        return support_extension_names.Contains(ext.ToLower());
                    }

                    var filePath = Directory.EnumerateFiles(path, actualSearchPattern).Where(checkExt).FirstOrDefault();
                    if (filePath is null)
                    {
                        //try to search sub
                        filePath = Directory.EnumerateDirectories(path)
                            .SelectMany(subPath => Directory.EnumerateFiles(subPath, actualSearchPattern))
                            .Where(checkExt)
                            .FirstOrDefault();

                        if (filePath is null)
                            ThreadLocalLogger.Instance.Log($".bms require audio file {audioPathName} is not found, ignored. search pattern: {Path.Combine(path, actualSearchPattern)}");
                        else
                            ThreadLocalLogger.Instance.Log($".bms require audio file {audioPathName} is found but it is located in a sub folder: {Path.GetDirectoryName(filePath)}");
                    }

                    return filePath;
                }

                var chart = bms_file_path.EndsWith(".bmson", StringComparison.InvariantCultureIgnoreCase) ? new BMSONDecoder(LongNote.TYPE_LONGNOTE) as ChartDecoder : new BMSDecoder();
                var model = chart.decode(bms_file_path, encoding);
                var notes = model.getAllTimeLines()
                    .SelectMany(x =>
                        x.getBackGroundNotes().Concat(x.getNotes()))
                    .OfType<Note>()
                    .SelectMany(x =>
                    {
                        return x.getLayeredNotes().Append(x);
                    })
                    .OrderBy(x => x.getMicroTime())
                    .Distinct()
                    .ToArray();
                var wavList = model.getWavList();

                var audio_map = wavList
                    .Select((x, i) => new
                    {
                        resourceId = i,
                        dataPath = x
                    })
                    .Select(x => (x.resourceId, searchAudioFile(x.dataPath), x.dataPath))
                    .Select(x => (x.resourceId, LoadAudio(x.Item2, x.dataPath)))
                    .Where(x => x.Item2 is int)
                    .ToDictionary(x => x.resourceId, x => x.Item2.Value);

                if (audio_map.Count == 0)
                    throw new Exception("audio_map is empty");

                var bms_evemts = notes
                    .Where(x => audio_map.ContainsKey(x.getWav()))//filter
                    .OrderBy(x => x.getMicroTime())
                    .ToArray();

                //init mixer
                mixer = BassMix.CreateMixerStream(48000, 2, BassFlags.Decode | BassFlags.MixerNonStop);

                //build triggers
                var mixer_events = new List<MixEventBase>(bms_evemts.Select(x =>
                {
                    var wavId = x.getWav();
                    var audioHandle = audio_map[wavId];
                    var audioLen = Bass.ChannelGetLength(audioHandle);
                    if (audioLen < 0)
                    {
                        var audioName = wavList.ElementAtOrDefault(wavId);
                        ThreadLocalLogger.Instance.Log($"Can't load and parse wavId {wavId} (becuase audioLen < 0), audioName = {audioName}, ignored.");
                        return default;
                    }
                    return new AudioMixEvent()
                    {
                        Time = TimeSpan.FromMilliseconds(x.getMilliTime()),
                        Duration = TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(audioHandle, audioLen)),
                        PlayOffset = TimeSpan.Zero,
                        WavId = wavId,
                        AudioHandle = audioHandle
                    };
                }).OfType<AudioMixEvent>());

                /*
                foreach (var @event in bms_evemts)
                {
                    ThreadLocalLogger.Instance.Log($"{@event.GetType().Name.Replace("Note", "")} {TimeSpan.FromMilliseconds(@event.getMilliTime()).TotalMilliseconds / 1000.0f}    {NumberToString(@event.getWav())}    {wavList[@event.getWav()]}");
                }
                foreach (var @event in mixer_events)
                {
                    ThreadLocalLogger.Instance.Log($"{@event.GetType().Name.Replace("MixEvent", "")} {@event.Time} {@event switch { AudioMixEvent a=> " + "+ (int)a.Duration.TotalMilliseconds + "   " + wavList[a.WavId],_ => "",}}");
                }
                */

                if (mixer_events.Count == 0)
                    throw new Exception("mixer_events is empty");

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

                ThreadLocalLogger.Instance.Log($"Actual clip({(int)full_audio_duration.TotalMilliseconds}ms):{(int)actual_start_time.TotalMilliseconds}ms ~ {(int)actual_end_time.TotalMilliseconds}ms");

                #endregion

                if (fast_clip)
                    FastClipEvent(mixer_events, ref actual_start_time, ref actual_end_time);

                //add special events to control encorder and mixer
                mixer_events.Add(new StopMixEvent { Time = actual_end_time });
                mixer_events.Add(new StartMixEvent() { Time = actual_start_time });

                //save encoder handle
                IAudioEncoder encoder = default;

                #region apply fade in/out

                var effect = new VolumeParameters();
                var fx = Bass.ChannelSetFX(mixer, effect.FXType, 0);

                if (fade_in != 0)
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
                        if (evt is StopMixEvent && (encoder?.IsEncoding ?? false))
                        {
                            Bass.ChannelStop(mixer);
                            encoder.EndEncode();
                        }
                        else if (evt is StartMixEvent && !(encoder?.IsEncoding ?? false))
                        {
                            var output_path = Path.Combine(dir_path, save_file_name);

                            encoder = encoding_type == 0 ? default : AudioEncoderFactory.CreateAudioEncoder(encoding_type);
                            encoder = encoder ?? AudioEncoderFactory.CreateAudioEncoder(output_path);

                            ThreadLocalLogger.Instance.Log($"Encoding output file path as {encoder?.GetType().Name} : {output_path}");

                            if (encoder is null)
                                throw new Exception($"Can't create encoder encodingType = {encoding_type}, skipped.");

                            var encParam = new EncodeParam()
                            {
                                EncodeOption = encoder_command_line,
                                EncodeOutputFilePath = output_path,
                                MixHandle = mixer
                            };
                            encoder.BeginEncode(encParam);
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

                ThreadLocalLogger.Instance.Log($"Success!");
                return true;
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

            int? LoadAudio(string audioFilePath, string hintDataPath = default, int task_index = -1)
            {
                if (!File.Exists(audioFilePath))
                {
                    if (!ignore_audio_missing)
                        throw new Exception($"Audio file not found: {audioFilePath} (hintDataPath : {hintDataPath})");

                    ThreadLocalLogger.Instance.Log($"[{task_index}] Audio file not found: {audioFilePath} (hintDataPath : {hintDataPath}) , ignored.");
                    return default;
                }

                var buffer = File.ReadAllBytes(audioFilePath);

                //var handle = BassOpus.CreateStream(buffer, 0, 0, BassFlags.Decode | BassFlags.Float);
                var handle = Bass.CreateStream(buffer, 0, buffer.LongLength, BassFlags.Decode | BassFlags.Float);
                if (handle == 0)
                    handle = BassOpus.CreateStream(buffer, 0, buffer.LongLength, BassFlags.Decode | BassFlags.Float);

                if (handle == 0)
                    return default;

                created_audio_handles.Add(handle);

                return handle;
            }
        }


        private static ConcurrentDictionary<string, Encoding> cachedEncoding = new();
        private static Encoding DetectEncoding(string bms_file_path)
        {
            using var fs = File.OpenRead(bms_file_path);
            var detector = new CharsetDetector();
            detector.Feed(fs);
            detector.DataEnd();

            var charset = detector.Charset;

            if (charset != null)
            {
                if (cachedEncoding.TryGetValue(charset, out var encoding))
                    return encoding;

                try
                {
                    encoding = Encoding.GetEncoding(charset);
                    ThreadLocalLogger.Instance.Log($"detected new charset:{charset}");
                }
                catch (Exception e)
                {
                    encoding = default;
                    ThreadLocalLogger.Instance.Log($"detected new charset {charset} but can't load: {e.Message}, it will return default.");
                }

                if (encoding != null)
                    return cachedEncoding[charset] = encoding;
            }

            return Encoding.UTF8;
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

            ThreadLocalLogger.Instance.Log($"Fast clip:remove {remove_count} events,now is {(int)actual_start_time.TotalMilliseconds}ms ~ {(int)actual_end_time.TotalMilliseconds}ms");
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
