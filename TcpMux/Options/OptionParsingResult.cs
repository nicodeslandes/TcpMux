using System;

namespace TcpMux.Options
{
    public abstract class OptionParsingResult
    {
        public static OptionParsingResult NotEnoughArguments() => new NotEnoughArguments();
        public static OptionParsingResult Error(string error) => new ArgumentParsingError(error);

        public static OptionParsingResult Success(TcpMuxOptions options) => new ParsingSuccess(options);
    }

    public class ArgumentParsingError : OptionParsingResult
    {
        public ArgumentParsingError(string error)
        {
            Error = error;
        }

        public new string Error { get; }

        public void Deconstruct(out string error) { error = Error; }
    }

    public class ParsingSuccess : OptionParsingResult
    {
        public ParsingSuccess(TcpMuxOptions options)
        {
            Options = options;
        }

        public TcpMuxOptions Options { get; }

        public void Deconstruct(out TcpMuxOptions options) { options = Options; }
    }

    public class NotEnoughArguments : OptionParsingResult { }
}