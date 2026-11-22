using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RedControlUtility.Controls;

public sealed class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 8;
    public double VerticalSpacing { get; set; } = 8;

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
    {
        double maxWidth = double.IsInfinity(available.Width) ? double.MaxValue : available.Width;
        double x = 0, rowHeight = 0, totalHeight = 0, usedWidth = 0;
        foreach (var child in Children)
        {
            child.Measure(new Windows.Foundation.Size(maxWidth, double.PositiveInfinity));
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > maxWidth)
            {
                totalHeight += rowHeight + VerticalSpacing;
                x = 0;
                rowHeight = 0;
            }

            x += s.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, s.Height);
            usedWidth = Math.Max(usedWidth, x - HorizontalSpacing);
        }

        return new Windows.Foundation.Size(usedWidth, totalHeight + rowHeight);
    }

    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > finalSize.Width)
            {
                y += rowHeight + VerticalSpacing;
                x = 0;
                rowHeight = 0;
            }

            child.Arrange(new Windows.Foundation.Rect(x, y, s.Width, s.Height));
            x += s.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, s.Height);
        }

        return new Windows.Foundation.Size(finalSize.Width, y + rowHeight);
    }
}
