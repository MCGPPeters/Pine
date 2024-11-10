// namespace Counter;

// public readonly record struct Model(int Counter);

// public interface Command;

// public readonly record struct Increment : Command;

// public readonly record struct Decrement : Command;

// public static class Counter
// {
//     public static Model Update(Model model, Command command) =>
//         command switch
//         {
//             Increment => model with { Counter = model.Counter + 1 },
//             Decrement => model with { Counter = model.Counter - 1 },
//             _ => model
//         };
// }