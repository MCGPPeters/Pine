using static Pine.Parse;

public static class Parse
{
    public static Parser<Route> Route<TState>() =>
        Home.Or(About).Or(Counter).Or(NotFound);

    /// <summary>
    /// Parses the home route. The url is /
    /// </summary>
    public static Parser<Route> Home =
        from _ in Char('/')
        from __ in EndOfInput
        select new Routes.Home() { State = new HomeState() } as Route;

    /// <summary>
    /// Parses the about route. The url is /about
    /// </summary>
    public static Parser<Route> About =
        from _ in Char('/')
        from __ in String("about")
        from ___ in EndOfInput
        select new Routes.About() { State = new AboutState() } as Route;

    /// <summary>
    /// Parses the counter route. The url is /counter
    /// </summary>
    public static Parser<Route> Counter =
        from _ in Char('/')
        from __ in String("counter")
        from ___ in EndOfInput
        select new Routes.Counter() { State = new CounterState() } as Route;
    public static Parser<Route> NotFound =
        from _ in EndOfInput
        select new Routes.NotFound() { State = new NotFoundState() } as Route;
}


