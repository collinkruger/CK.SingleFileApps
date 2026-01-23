// Screensaver: A simple terminal screensaver, reminiscent of the old bouncing DVD logo.

// Setup
Console.CursorVisible = false;
Console.Clear();

int x = Console.WindowWidth  / 2;
int y = Console.WindowHeight / 2;
int dx = 1;
int dy = 1;

string[] art = [
    @" ___ __   __ ___  ",
    @"|   \\ \ / /|   \ ",
    @"| |) |\ V / | |) |",
    @"|___/  \_/  |___/ ",
];

int artHeight = art.Length;
int artWidth  = art.Max(x => x.Length);

string clear = new string(' ', artWidth);

// Handle Ctrl+C for graceful exit
Console.CancelKeyPress += (_, e) =>
{
    Console.CursorVisible = true;
    Console.Clear();
};

// Animation loop
while (true)
{
    int width  = Console.WindowWidth;
    int height = Console.WindowHeight;

    // Clear old position
    for (int i = 0; i < artHeight; i++)
    {
        Console.SetCursorPosition(x, y + i);
        Console.Write(clear);
    }

    // Update position
    x += dx;
    y += dy;

    // Bounce off walls
    if (x <= 0 || x + artWidth  >= width)  dx = -dx;
    if (y <= 0 || y + artHeight >= height) dy = -dy;

    // Clamp position to valid range (handles terminal resize)
    x = Math.Clamp(x, 0, width  - artWidth);
    y = Math.Clamp(y, 0, height - artHeight);

    // Draw at new position
    for (int i = 0; i < artHeight; i++)
    {
        Console.SetCursorPosition(x, y + i);
        Console.Write(art[i]);
    }

    // Consume any key presses without displaying them
    while (Console.KeyAvailable)
        Console.ReadKey(true);

    Thread.Sleep(66);
}
