using System;
using System.Drawing;
using System.Windows.Forms;
using EveOPreview.Services.Interop;

namespace EveOPreview.View
{
    /// <summary>
    /// 全屏透明覆盖 + 鼠标拖框 ROI 选择器。
    /// 完全匹配 EVEAutoWarning 的实现方式。
    /// </summary>
    public sealed class RoiSelectorForm : Form
    {
        private Point _start;
        private Point _end;
        private bool _dragging;
        private readonly IntPtr _targetHwnd;

        /// <summary>客户区相对坐标（相对于 _targetHwnd 的客户区）</summary>
        public Rectangle SelectedRegion { get; private set; } = Rectangle.Empty;

        public RoiSelectorForm(IntPtr targetHwnd)
        {
            _targetHwnd = targetHwnd;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            // 使用近乎透明的黑色作为 key，让窗体"看不见"但仍能捕获鼠标事件
            this.BackColor = Color.FromArgb(1, 0, 0);
            this.TransparencyKey = Color.FromArgb(1, 0, 0);
            this.Opacity = 0.3;
            this.TopMost = true;
            this.Cursor = Cursors.Cross;
            this.DoubleBuffered = true;
            this.KeyPreview = true;
            this.ShowInTaskbar = false;

            this.KeyDown += OnKeyDown;
            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
            this.Paint += OnPaint;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _start = e.Location;
            _end = e.Location;
            _dragging = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _end = e.Location;
            this.Invalidate();
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = false;

            int x = Math.Min(_start.X, _end.X);
            int y = Math.Min(_start.Y, _end.Y);
            int w = Math.Abs(_end.X - _start.X);
            int h = Math.Abs(_end.Y - _start.Y);

            if (w <= 10 || h <= 10)
            {
                // 选区太小，取消
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                return;
            }

            var screenRect = new Rectangle(x, y, w, h);

            // 将屏幕坐标转换为目标窗口的客户区坐标
            if (_targetHwnd != IntPtr.Zero)
            {
                var clientTopLeft = new POINT(0, 0);
                User32NativeMethods.ClientToScreen(_targetHwnd, ref clientTopLeft);
                SelectedRegion = new Rectangle(
                    screenRect.X - clientTopLeft.X,
                    screenRect.Y - clientTopLeft.Y,
                    screenRect.Width,
                    screenRect.Height);
            }
            else
            {
                SelectedRegion = screenRect;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (!_dragging) return;
            int x = Math.Min(_start.X, _end.X);
            int y = Math.Min(_start.Y, _end.Y);
            int w = Math.Abs(_end.X - _start.X);
            int h = Math.Abs(_end.Y - _start.Y);

            using (var brush = new SolidBrush(Color.FromArgb(60, 0, 120, 255)))
            {
                e.Graphics.FillRectangle(brush, x, y, w, h);
            }
            using (var pen = new Pen(Color.Red, 2))
            {
                e.Graphics.DrawRectangle(pen, x, y, w, h);
            }
        }
    }
}
