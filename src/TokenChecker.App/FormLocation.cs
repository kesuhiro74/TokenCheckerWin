namespace TokenChecker.App;

internal readonly record struct FormLocation(int X, int Y)
{
    public Point ToPoint() => new(X, Y);

    public static FormLocation FromPoint(Point point) => new(point.X, point.Y);
}
