using System;

namespace HkuLibrarySeatBooking
{
    internal static class TestProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string reportPath = args.Length > 1 && args[0] == "--self-test"
                ? args[1]
                : null;
            return CoreTests.Run(reportPath);
        }
    }
}
