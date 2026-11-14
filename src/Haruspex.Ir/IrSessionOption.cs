namespace Haruspex.Ir;

/// <summary>
/// An option the callers' sessions are declared to set, which the claims of a document rest on (0.17). Its name is the
/// source dialect's, as the config file writes it: for <c>tsql</c>, a <c>SET</c> option such as <c>ansiWarnings</c>.
/// </summary>
/// <param name="Name">The option, in the dialect's terms.</param>
/// <param name="On">Whether it is declared on.</param>
public sealed record IrSessionOption(string Name, bool On);
