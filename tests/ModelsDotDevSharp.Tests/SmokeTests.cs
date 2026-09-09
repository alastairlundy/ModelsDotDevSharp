using ModelsDotDevSharp.Extensions;

namespace ModelsDotDevSharp.Tests;

public class SmokeTests
{
    [Test]
    public async Task LibraryTypeIsResolvable()
    {
        Type provider = typeof(ModelsDevServiceCollectionExtensions);
        await Assert.That(provider).IsNotNull();
    }
}
