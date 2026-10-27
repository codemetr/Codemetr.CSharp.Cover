using Xunit;
public class T { [Fact] public void Local() => Assert.Equal(6, new Shop.Calc().Local(2)); }
