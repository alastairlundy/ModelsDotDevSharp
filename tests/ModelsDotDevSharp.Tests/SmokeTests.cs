using TUnit.Assertions;
using TUnit.Core;

namespace ModelsDotDevSharp.Tests;

public class SmokeTests
{
    [Test]
    public async Task LibraryTypeIsResolvable()
    {
        var provider = typeof(ModelsDotDevSharp.ModelsDevServiceCollectionExtensions);
        await Assert.That(provider).IsNotNull();
    }
}
