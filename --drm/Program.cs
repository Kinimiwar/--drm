using __drm.Modules;
using System;
using System.Linq;

namespace __drm {
    class Program {
        static void Main(string[] args) {
            Console.WriteLine();
            Console.WriteLine("Super super fucking early version. Stuff will probably break.\n  DIRECTV is the only tested AP Auth MSO.");
            // Parse Arguments
            Arguments.Parse(args);
            if(Arguments.URL == null) {
                Logger.Debug(
                    "Supported Modules:\n" +
                    "  " + string.Join("\n  ", Enum.GetNames(typeof(Common.Module)).Select((x, i) => "#" + (i+1).ToString() + " | " + x))
                );
                Logger.Debug("Need support on one? Which one? (select by the #)");
                try {
                    Activator.CreateInstance(Type.GetType("__drm.Modules." + Enum.GetNames(typeof(Common.Module))[int.Parse(new string(Console.ReadLine().Where(char.IsDigit).ToArray())) - 1].ToLower(), true)).ToString();
                } catch {
                    Logger.Error("Invalid # that you entered :/");
                }
                Console.ReadLine();
            }
            string t = Arguments.URL.ToLower();
            if (t.Contains("simpsonsworld.com")) {
                new simpsonsworld();
            }
            if (t.Contains("disneynow.go.com")) {
                new disneynow().Start();
            }
            Console.WriteLine("\nFinished...");
            Console.ReadLine();
        }
        static void Router(string url) {
        }
    }
}
