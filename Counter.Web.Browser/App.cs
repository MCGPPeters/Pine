using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;


namespace Counter.Web.Browser;

public record Button<TCommand> where TCommand : Command
{
    public required string Text { get; init; }
    public required TCommand Command { get; init; }
}

public record CommandButtonComponent<TCommand> : Application<CommandButtonComponent<TCommand>, Button<TCommand>, TCommand>
     where TCommand : class, Command
{
    public CommandButtonComponent(Button<TCommand> original) : base(original)
    {

    }

    public override Node View(Button<TCommand> state)
   => button(
            [
                new Property { Name = "type", Value = "button" },
                   onclick(state.Command)
            ],
            [
                text(state.Text)
            ]
        );

    protected override Button<TCommand> Update(Button<TCommand> state, TCommand command)
    {
        throw new NotImplementedException();
    }
}

public record Model(int Counter);

public interface CounterCommand : Command { }

public record Increment : CounterCommand { }

public record Decrement : CounterCommand { }

public record CounterComponent : Application<CounterComponent, Model, CounterCommand>
{
    public CounterComponent(Model model) : base(model)
    {
        // Initialization moved to InitializeAsync
    }

    public override Node View(Model state) =>
        div([],
        [
                new CommandButtonComponent<Increment>(new Button<Increment>{ Text = "+", Command = new Increment()}){ Id = "48" }.View(new Button<Increment>{ Text = "+", Command = new Increment()}),
                button(
                    [
                        new Property { Name = "type", Value = "button" },
                        onclick(new Decrement())
                    ],
                    [
                        text("-")
                    ]
                ),
                text(state.Counter.ToString())
        ]);

    protected override Model Update(Model state, CounterCommand command)
    {
        return command switch
        {
            Increment => state with { Counter = state.Counter + 1 },
            Decrement => state with { Counter = state.Counter - 1 },
            _ => state
        };
    }
}

public partial class Runtime
{
    private static CounterComponent _application = new CounterComponent(new Model(0)) { Id = "1000" };

    [JSExport]
    public static async Task Dispatch(string commandId)
    {
        Console.WriteLine($"Dispatching command: {commandId}");
        if (_application == null)
        {
            throw new InvalidOperationException("Application not initialized.");
        }
        await _application.Dispatch(commandId);
    }

    [JSExport]
    public static async Task Start()
    {
        await _application.InitializeAsync();
    }
}