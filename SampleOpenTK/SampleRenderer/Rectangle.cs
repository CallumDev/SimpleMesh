namespace SampleOpenTK
{
    public struct Rectangle
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public Rectangle(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(int x, int y) => x >= X && x <= (X + Width) && y >= Y && y <= (Y + Height);
    }
}