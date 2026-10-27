using NUnit.Framework;
public class T { [Test] public async Task Load() => Assert.That(await new Shop.Calc().LoadAsync(3), Is.EqualTo(3)); }
