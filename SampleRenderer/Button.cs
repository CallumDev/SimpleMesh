using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace SampleRenderer;

public class Button
{
    public string Text;
    public int X;
    public int Y;
    public Action Clicked;

    public Button(string text, int x, int y, Action clicked)
    {
        Text = text;
        X = x;
        Y = y;
        Clicked = clicked;
    }

    public void Render(Render2D text)
    {
        var bSz = text.MeasureString(Text) + new Vector2i(4, 4);
        text.FillBackground(X, Y, bSz.X, bSz.Y);
        text.DrawString(Text, X + 2, Y + 2);
    }

    private bool isDown = false;
    public void OnMouseDown(MouseButtonEventArgs e, Vector2 mousePosition, Render2D text)
    {
        if (e.Button != MouseButton.Left) return;
        isDown = false;
        var bSz = text.MeasureString(Text) + new Vector2i(4, 4);
        var rect = new Rectangle(X, Y, bSz.X, bSz.Y);
        if (rect.Contains((int) mousePosition.X, (int) mousePosition.Y))
        {
            isDown = true;
        }
    }

    public void OnMouseUp(MouseButtonEventArgs e, Vector2 mousePosition, Render2D text)
    {
        if(!isDown || e.Button != MouseButton.Left) return;
        isDown = false;
        var bSz = text.MeasureString(Text) + new Vector2i(4, 4);
        var rect = new Rectangle(X, Y, bSz.X, bSz.Y);
        if (rect.Contains((int) mousePosition.X, (int) mousePosition.Y))
        {
            Clicked();
        }
    }

}