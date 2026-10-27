namespace Shop;

public class Calc
{
    public int Abs(int a)
    {
        if (a < 0)
            return -a;
        return a;
    }

    public int Never(int a)
    {
        var b = a * 2;
        return b + 1;
    }

    public async Task<int> LoadAsync(int a)
    {
        await Task.Yield();
        if (a > 10) return 10;
        return a;
    }

    public IEnumerable<int> Items(int n)
    {
        for (var i = 0; i < n; i++)
            yield return i;
    }

    public int Local(int a)
    {
        return Twice(a) + Lambda(a);
        int Twice(int x) => x * 2;
    }

    static readonly Func<int, int> LambdaField = x => x + 1;
    public int Lambda(int a) => new[] { a }.Select(x => x > 0 ? x : 0).Sum();

    public string Name { get; set; } = "";
    public int Total => Name.Length;
}
