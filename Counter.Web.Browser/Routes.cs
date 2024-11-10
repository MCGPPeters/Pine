public static class Routes
{
    public record Home : Route<HomeState>;

    public record About : Route<AboutState>;

    public record NotFound : Route<NotFoundState>;

    public record Counter : Route<CounterState>;

}


