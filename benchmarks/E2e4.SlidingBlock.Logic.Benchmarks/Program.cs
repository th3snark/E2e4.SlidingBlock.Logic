using System.Diagnostics;
using E2e4.SlidingBlock.Logic;
using E2e4.SlidingBlock.Logic.Benchmarks;

// dotnet run -c Release --project benchmarks/E2e4.SlidingBlock.Logic.Benchmarks
// Best of several runs; allocation is the managed bytes one run allocates on this thread.
foreach (var (name, run) in Scenarios())
{
    for (var warm = Stopwatch.StartNew(); warm.ElapsedMilliseconds < 400;) run();   // long enough for the JIT to reach its optimised code
    double best = double.MaxValue;
    long bytes = 0;
    for (int i = 0; i < 7; i++)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        run();
        sw.Stop();
        bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
    }
    Console.WriteLine($"{name,-34}{best,10:F2} ms{bytes / 1024.0,12:F0} KiB");
}

static IEnumerable<(string, Action)> Scenarios()
{
    var solver = new PuzzleSolver();
    var klotski = Fixtures.CreateKlotski();
    var rush = Fixtures.CreateRushHour();
    var lab2 = Fixtures.CreateLab2();
    var p15 = Fixtures.CreatePuzzle15();
    var ice = Fixtures.CreateIce().Domain;
    var carrier = Fixtures.CreateCarrier().Domain;
    var hard15 = PuzzleWalk.RandomWalk(p15, p15.Goal, 200, new Random(3));

    yield return ("BFS Klotski (81 moves)", () => solver.SolveBfs(klotski, klotski.InitialState));
    yield return ("BFS Rush Hour", () => solver.SolveBfs(rush, rush.InitialState));
    yield return ("BFS Lab 2 polyominoes", () => solver.SolveBfs(lab2, lab2.InitialState));
    yield return ("A* 15-puzzle", () => solver.SolveAStar(p15, PuzzleWalk.RandomWalk(p15, p15.Goal, 40, new Random(15)), p15.ManhattanDistance));
    yield return ("IDA* 15-puzzle", () => new PuzzleSolver { MaxNodes = 20_000_000 }.SolveIdaStar(p15, hard15, p15.ManhattanDistance));
    yield return ("GenerateMoves Klotski x200k", () => Generate(klotski, 200_000));
    yield return ("GenerateMoves Rush Hour x200k", () => Generate(rush, 200_000));
    yield return ("GenerateMoves Lab 2 x200k", () => Generate(lab2, 200_000));
    yield return ("GenerateMoves Lab 3 ice x200k", () => Generate(ice, 200_000));
    yield return ("GenerateMoves Lab 3 carrier x200k", () => Generate(carrier, 200_000));
}

static void Generate<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain, int times) where TState : struct, IEquatable<TState>
{
    var buffer = new TMove[domain.MaxMoves];
    var state = PuzzleWalk.RandomWalk(domain, domain.InitialState, 30, new Random(1));
    for (int i = 0; i < times; i++) domain.GenerateMoves(state, buffer);
}
