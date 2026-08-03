using System;

namespace InfiniteSplitter
{
    /// <summary>
    /// Success or a <see cref="RefreshError"/>. Nothing in the memory layer throws
    /// across a boundary: an exception escaping into LiveSplit's render loop takes the
    /// whole timer down, and "the game is not running" is an ordinary state.
    /// </summary>
    public readonly struct Result
    {
        public RefreshError Error { get; }

        private Result(RefreshError error) { Error = error; }

        public bool IsOk => Error == null;
        public bool IsErr => Error != null;

        public static Result Ok() => new Result(null);

        public static Result Err(RefreshError error) => new Result(error);

        public static Result Err(RefreshErrorReason reason, string message = null, Exception exception = null) =>
            new Result(new RefreshError(reason, message, exception));
    }

    public readonly struct Result<T>
    {
        public T Value { get; }
        public RefreshError Error { get; }

        private Result(T value, RefreshError error) { Value = value; Error = error; }

        public bool IsOk => Error == null;
        public bool IsErr => Error != null;

        public static Result<T> Ok(T value) => new Result<T>(value, null);

        public static Result<T> Err(RefreshError error) => new Result<T>(default(T), error);

        public static Result<T> Err(RefreshErrorReason reason, string message = null, Exception exception = null) =>
            new Result<T>(default(T), new RefreshError(reason, message, exception));

        /// <summary>Drops the value, keeping only success/failure.</summary>
        public Result Discard() => IsOk ? Result.Ok() : Result.Err(Error);
    }
}
