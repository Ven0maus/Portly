namespace Portly.Tests
{
    [SetUpFixture]
    internal class GlobalTearDown
    {
        [OneTimeTearDown]
        public void AfterAllTests()
        {
            // Cleanup tests container directory
            if (Directory.Exists("PortlyTests"))
                Directory.Delete("PortlyTests", true);
        }
    }
}
