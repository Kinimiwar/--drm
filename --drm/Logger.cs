using System;

namespace __drm {
    class Logger {
        private static void L(string msg) {
            Console.WriteLine(" " + msg);
            Console.ResetColor();
        }
        public static void Error(string msg) {
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.DarkRed;
            L(msg);
        }
        public static void Info(string msg) {
            Console.ForegroundColor = ConsoleColor.Cyan;
            L(msg);
        }
        public static void Debug(string msg) {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            L(msg);
        }
    }
}
