using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;


namespace Counter.Web.Browser;

public record CounterComponent : Component<CounterComponent, Model, Command>
{
    public CounterComponent(Model model) : base(model)
    {
        
    }

    public override Node View(Model state) =>
        div([],
        [
            button(
            [
                new Property { Name = "type", Value = "button" },
                onclick(new Increment())
            ],
            [
                text("+")
            ]),

            button([
                new Property { Name = "type", Value = "button" },
                onclick(new Decrement())
            ], 
            [
                text("-")
            ]),
            text(state.Counter.ToString())

        ]);

    protected override Model Update(Model state, Command command)
    {
        return command switch
        {
            Increment => state with { Counter = state.Counter + 1 },
            Decrement => state with { Counter = state.Counter - 1 },
            _ => state
        };
    }
}

public partial class App
{

    private static CounterComponent _application = new(new Model(0)){ Id = "1000", Tag = "" };

    [JSExport]
    public static async Task Dispatch(string commandId)
    {       
        await _application.Dispatch(commandId);
    }

    [JSExport]
    public static async Task Start()
    {
        var patches = CounterComponent.Delta(span([], []), _application.VirtualDom);

        foreach (var patch in patches)
        {
            await _application.Apply(patch);
        }
    }
}


