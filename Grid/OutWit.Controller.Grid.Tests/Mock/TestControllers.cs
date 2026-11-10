using System;
using System.IO;

namespace OutWit.Controller.Grid.Tests.Mock
{
    internal static class TestControllers
    {
        #region Constants

        private const string CONTROLLERS_FOLDER = "@Controllers";

        #endregion

        #region Functions

        /// <summary>
        /// Finds the module folder for the engine under test: the one staged next to the test
        /// binaries, which honours the project's excluded modules. The shared solution folder may
        /// hold modules from other builds that this rig cannot host.
        /// </summary>
        /// <returns>The folder, or null when none exists.</returns>
        public static string? FindPath()
        {
            var staged = Path.Combine(AppContext.BaseDirectory, CONTROLLERS_FOLDER);
            if (Directory.Exists(staged))
                return staged;

            var dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                var candidate = Path.Combine(dir, CONTROLLERS_FOLDER, "Debug");
                if (Directory.Exists(candidate))
                    return candidate;

                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }

        #endregion
    }
}
