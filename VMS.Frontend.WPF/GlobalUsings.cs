// UseWPF and UseWindowsForms (added for Video Wall's monitor enumeration, System.Windows.Forms.Screen)
// each contribute an implicit global "using" for their own root namespace, and both namespaces
// declare types with the same short name (Application, Brush, ...) — these aliases resolve the
// ambiguity project-wide instead of qualifying every call site individually.
global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Color = System.Windows.Media.Color;
global using MessageBox = System.Windows.MessageBox;
global using DataGridCell = System.Windows.Controls.DataGridCell;
global using CheckBox = System.Windows.Controls.CheckBox;
global using DataGrid = System.Windows.Controls.DataGrid;
global using Point = System.Windows.Point;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using Brushes = System.Windows.Media.Brushes;
global using Cursors = System.Windows.Input.Cursors;
global using SolidColorBrush = System.Windows.Media.SolidColorBrush;
global using DragEventArgs = System.Windows.DragEventArgs;
global using DragDropEffects = System.Windows.DragDropEffects;
global using DataFormats = System.Windows.DataFormats;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
