using System.Text.Json;

namespace WhiteBoard.Core.Domain.Objects;

/// <summary>表格对象：行列数据以行主序 string[] 存储，JSON 序列化后写入 PageSerializer Payload。</summary>
public sealed class TableObject : WhiteboardObject
{
    public TableData Data { get; set; }

    public TableObject(TableData data, float x, float y, float width, float height)
    {
        Data = data;
        SetBounds(x, y, width, height);
    }

    public TableObject(Guid id, TableData data,
        float bx, float by, float bw, float bh, float[] transform)
        : base(id, bx, by, bw, bh, transform)
    {
        Data = data;
    }
}

public sealed class TableData
{
    public int Rows { get; set; }
    public int Cols { get; set; }
    public string[] Cells { get; set; } = [];   // row-major: [row*Cols + col]

    public TableData() { }

    public TableData(int rows, int cols)
    {
        Rows = rows;
        Cols = cols;
        Cells = new string[rows * cols];
        Array.Fill(Cells, "");
    }

    public string GetCell(int row, int col) => Cells[row * Cols + col];
    public void SetCell(int row, int col, string value) => Cells[row * Cols + col] = value;

    public string ToJson() => JsonSerializer.Serialize(this);
    public static TableData FromJson(string json) =>
        JsonSerializer.Deserialize<TableData>(json) ?? new TableData();
}
