public static class Counter
{
    public static Model Update(Model model, Command command) =>
        command switch
        {
            Increment => model with { Counter = model.Counter + 1 },
            Decrement => model with { Counter = model.Counter - 1 },
            _ => model
        };
}


