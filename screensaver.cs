// Screensaver: A simple terminal screensaver, reminiscent of the old bouncing DVD logo.

// Setup
Console.CursorVisible = false;
Console.Clear();

int x = Console.WindowWidth  / 2;
int y = Console.WindowHeight / 2;
int dx = 1;
int dy = 1;

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
    Console.SetCursorPosition(x, y);
    Console.Write(' ');

    // Update position
    x += dx;
    y += dy;

    // Bounce off walls
    if (x <= 0 || x >= width - 1)  dx = -dx;
    if (y <= 0 || y >= height - 1) dy = -dy;

    // Clamp position to valid range (handles terminal resize)
    x = Math.Clamp(x, 0, width  - 1);
    y = Math.Clamp(y, 0, height - 1);

    // Draw at new position
    Console.SetCursorPosition(x, y);
    Console.Write('@');

    // Consume any key presses without displaying them
    while (Console.KeyAvailable)
        Console.ReadKey(true);

    Thread.Sleep(50);
}
