using Portly.Tests.Helpers;
using System.Collections.Concurrent;

namespace Portly.Tests
{
    [Parallelizable(ParallelScope.All)]
    internal abstract class BaseTests
    {
        private readonly ConcurrentDictionary<string, (string serverDir, string clientDir)> _testDirectories = new();

        /// <summary>
        /// Shortcut to localhost ip, use this to prevent dns lookups
        /// </summary>
        protected const string LocalHost = "127.0.0.1";

        /// <summary>
        /// Represents the test's unique client directory
        /// </summary>
        protected string ClientDirectory => _testDirectories[TestContext.CurrentContext.Test.ID].clientDir;
        /// <summary>
        /// Represents the test's unique server directory
        /// </summary>
        protected string ServerDirectory => _testDirectories[TestContext.CurrentContext.Test.ID].serverDir;

        [SetUp]
        public async Task SetUp()
        {
            var test = TestContext.CurrentContext.Test.ID;
            if (!_testDirectories.ContainsKey(test))
            {
                // Create new test directory
                _testDirectories[test] = Tools.CreateIsolatedTestDirectories();
            }
        }

        [TearDown]
        public async Task TearDown()
        {
            // Cleanup test directory
            var test = TestContext.CurrentContext.Test.ID;
            if (_testDirectories.TryGetValue(test, out var dirs))
            {
                var parent = new DirectoryInfo(dirs.serverDir).Parent!.FullName;
                if (Directory.Exists(parent))
                    Directory.Delete(parent, true);
            }
        }
    }
}
