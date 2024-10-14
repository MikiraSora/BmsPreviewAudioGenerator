using System.Text;
using System.Threading;

namespace BmsPreviewAudioGenerator
{
    public class ThreadLocalLogger
    {
        private static ThreadLocal<ThreadLocalLogger> instance =
            new ThreadLocal<ThreadLocalLogger>(() => new ThreadLocalLogger());

        public static ThreadLocalLogger Instance => instance.Value;

        private StringBuilder sb = new();

        private ThreadLocalLogger()
        {

        }

        public string Prefix { get; set; } = string.Empty;

        public void Log(string message)
        {
            sb.AppendLine($"{Prefix}" + message);
        }

        public override string ToString()
        {
            return sb.ToString();
        }

        public void Clear()
        {
            sb.Clear();
        }
    }
}
