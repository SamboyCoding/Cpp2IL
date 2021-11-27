using System;
using System.Drawing;
using System.IO;
using Cpp2IL.Core;
using Pastel;

namespace Cpp2IL
{
    internal static class ConsoleLogger
    {
        internal static readonly Color VERB = Color.Gray;
        internal static readonly Color INFO = Color.LightBlue;
        internal static readonly Color WARN = Color.Yellow;
        internal static readonly Color ERROR = Color.DarkRed;

        internal static bool DisableColor { private get; set; }

        internal static bool ShowVerbose { private get; set; }

        private static bool LastNoNewline;

        public static void Initialize()
        {
            Logger.InfoLog += (message, source) => Write("Info", source, message, INFO);
            Logger.WarningLog += (message, source) => Write("Warn", source, message, WARN);
            Logger.ErrorLog += (message, source) => Write("Fail", source, message, ERROR);

            Logger.VerboseLog += (message, source) =>
            {
                if (ShowVerbose)
                    Write("Verb", source, message, VERB);
            };

            HarmonyLib.Tools.Logger.MessageReceived += (sender, args) =>
            {
                if (args.LogChannel is HarmonyLib.Tools.Logger.LogChannel.Warn)
                    Logger.WarnNewline(args.Message, "HarmonyInternal");
                if (args.LogChannel is HarmonyLib.Tools.Logger.LogChannel.Error)
                    Logger.ErrorNewline(args.Message, "HarmonyInternal");
            };

            HarmonyLib.Tools.Logger.ChannelFilter = HarmonyLib.Tools.Logger.LogChannel.Warn | HarmonyLib.Tools.Logger.LogChannel.Error;

            CheckColorSupport();
        }

        internal static void Write(string level, string source, string message, Color color)
        {
            if (!LastNoNewline)
                WritePrelude(level, source, color);

            LastNoNewline = !message.EndsWith("\n");

            if (!DisableColor)
                message = message.Pastel(color);

            Console.Write(message);
        }

        private static void WritePrelude(string level, string source, Color color)
        {
            var message = $"[{level}] [{source}] ";
            if (!DisableColor)
                message = message.Pastel(color);
            
            Console.Write(message);
        }

        public static void CheckColorSupport()
        {
            // if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            // {
            //     DisableColor = true;
            //     WarnNewline("Looks like you're running on a non-windows platform. Disabling ANSI color codes.");
            // }
            /*else*/
            if (Directory.Exists(@"Z:\usr\"))
            {
                DisableColor = true;
                Logger.WarnNewline("Looks like you're running in wine or proton. Disabling ANSI color codes.");
            }
            else if (Environment.GetEnvironmentVariable("NO_COLOR") != null)
            {
                DisableColor = true; //Just manually set this, even though Pastel respects the environment variable
                Logger.WarnNewline("NO_COLOR set, disabling ANSI color codes as you requested.");
            }
        }
    }
}
