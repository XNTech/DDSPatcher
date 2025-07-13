Imports System
Imports System.IO
Imports System.Text

Module DdsPatcher
    ' DDS 文件头标识
    Private ReadOnly DDS_HEADER As Byte() = {&H44, &H44, &H53, &H20} ' "DDS "
    Private ReadOnly POF_MARKER As String = "POF"
    Private autoPatchMode As Boolean = False

    Sub Main()
        Console.WriteLine("DDS 文件修补工具 by ChilorXN.")
        Console.WriteLine("使用说明: 源文件路径 修改的DDS路径 DDS序号(从1开始)")
        Console.WriteLine("示例: ""C:\files\model.afb"" ""C:\modified\dds_1.dds"" 1")
        Console.WriteLine("输入 'EnableAutoPatch' 跳过二次确认")
        Console.WriteLine("输入 'DisableAutoPatch' 恢复二次确认")
        Console.WriteLine("输入 'exit' 退出程序")

        ' 持续处理循环
        While True
            Console.WriteLine()
            Console.WriteLine($"当前模式: {(If(autoPatchMode, "自动修补模式（跳过二次确认）", "正常模式"))}")
            Console.Write("> ")
            Dim input As String = Console.ReadLine()

            ' 检查特殊命令
            Select Case input.Trim().ToLower()
                Case "exit"
                    Exit While
                Case "enableautopatch"
                    autoPatchMode = True
                    Console.WriteLine("已启用自动修补模式，将跳过确认直接进行强制修补！")
                    Continue While
                Case "disableautopatch"
                    autoPatchMode = False
                    Console.WriteLine("已停用自动修补模式，将恢复二次确认流程")
                    Continue While
            End Select

            ' 处理输入
            ProcessPatchCommand(input)
        End While

        Console.WriteLine("程序已退出")
    End Sub

    Private Sub ProcessPatchCommand(input As String)
        Dim args As String() = ParseCommandLine(input)

        If args.Length <> 3 Then
            Console.WriteLine("错误: 需要3个参数 - 源文件路径 修改的DDS路径 DDS序号")
            Return
        End If

        Dim sourceFile As String = args(0)
        Dim modifiedDdsFile As String = args(1)
        Dim ddsIndex As Integer

        ' 验证DDS序号
        If Not Integer.TryParse(args(2), ddsIndex) OrElse ddsIndex < 1 Then
            Console.WriteLine("错误: DDS序号必须是大于0的整数")
            Return
        End If

        ' 验证文件存在
        If Not File.Exists(sourceFile) Then
            Console.WriteLine($"错误: 源文件不存在 - {sourceFile}")
            Return
        End If

        If Not File.Exists(modifiedDdsFile) Then
            Console.WriteLine($"错误: 修改的DDS文件不存在 - {modifiedDdsFile}")
            Return
        End If

        ' 获取文件扩展名
        Dim extension As String = Path.GetExtension(sourceFile).ToLower()
        If extension <> ".afb" AndAlso extension <> ".svo" Then
            Console.WriteLine($"错误: 不支持的文件类型 - {extension} (仅支持 .afb 和 .svo)")
            Return
        End If

        Try
            PatchDdsFile(sourceFile, modifiedDdsFile, ddsIndex, extension = ".afb")
        Catch ex As Exception
            Console.WriteLine($"修补过程中出错: {ex.Message}")
        End Try
    End Sub

    Private Sub PatchDdsFile(sourceFile As String, modifiedDdsFile As String, ddsIndex As Integer, isAfbFile As Boolean)
        ' 读取源文件
        Dim sourceData As Byte() = File.ReadAllBytes(sourceFile)

        ' 查找所有DDS位置
        Dim ddsPositions As List(Of DdsInfo) = LocateAllDdsFiles(sourceData, isAfbFile)

        ' 检查请求的DDS索引是否有效
        If ddsIndex > ddsPositions.Count Then
            Console.WriteLine($"错误: 文件中只包含 {ddsPositions.Count} 个DDS文件，无法访问第 {ddsIndex} 个")
            Return
        End If

        Dim targetDds As DdsInfo = ddsPositions(ddsIndex - 1)

        ' 读取修改后的DDS文件
        Dim modifiedDdsData As Byte() = File.ReadAllBytes(modifiedDdsFile)

        ' 验证DDS头
        If modifiedDdsData.Length < 4 OrElse
           Not (modifiedDdsData(0) = DDS_HEADER(0) AndAlso
           modifiedDdsData(1) = DDS_HEADER(1) AndAlso
           modifiedDdsData(2) = DDS_HEADER(2) AndAlso
           modifiedDdsData(3) = DDS_HEADER(3)) Then
            Console.WriteLine("错误: 修改的DDS文件没有有效的DDS头")
            Return
        End If

        ' 验证大小
        If modifiedDdsData.Length <> targetDds.Length Then
            Console.WriteLine($"警告: DDS大小不匹配 (原: {targetDds.Length} 字节, 新: {modifiedDdsData.Length} 字节)")
            Console.WriteLine($"原DDS位置: 文件偏移 0x{targetDds.StartOffset:X8}")

            ' 如果新DDS比原DDS大，直接拒绝
            If modifiedDdsData.Length > targetDds.Length Then
                Console.WriteLine("错误: 新DDS比原DDS大，无法修补")
                Return
            End If

            ' 如果新DDS比原DDS小，根据模式处理
            If Not autoPatchMode Then
                Console.WriteLine("信息: 检测到新DDS比原DDS小，可尝试使用强制修补")
                Console.WriteLine("警告: 强制修补可能会导致问题!")
                Console.WriteLine("是否要使用强制修补? (y/n)")
                Dim response As String = Console.ReadLine().Trim().ToLower()

                If response <> "y" AndAlso response <> "yes" Then
                    Console.WriteLine("修补已取消")
                    Return
                End If

                ' 二次确认
                Console.WriteLine("确定要使用强制修补吗? 这可能会破坏文件结构! (yes/no)")
                Dim confirm As String = Console.ReadLine().Trim().ToLower()

                If confirm <> "yes" Then
                    Console.WriteLine("修补已取消")
                    Return
                End If
            Else
                Console.WriteLine("警告：检测到已启用自动修补模式，将跳过二次确认直接进行强制修补！")
            End If
        End If

        ' 创建备份
        Dim backupFile As String = sourceFile & ".bak"
        If Not File.Exists(backupFile) Then
            File.Copy(sourceFile, backupFile)
            Console.WriteLine($"已创建备份文件: {backupFile}")
        End If

        ' 执行修补
        If modifiedDdsData.Length < targetDds.Length Then
            ' 仅覆盖修改后的DDS部分，保留剩余部分不变
            Array.Copy(modifiedDdsData, 0, sourceData, targetDds.StartOffset, modifiedDdsData.Length)
            Console.WriteLine($"警告: 仅修补了前 {modifiedDdsData.Length} 字节，保留了原DDS的 {targetDds.Length - modifiedDdsData.Length} 字节未修改")
        Else
            ' 完全替换
            Array.Copy(modifiedDdsData, 0, sourceData, targetDds.StartOffset, targetDds.Length)
        End If

        ' 保存修改后的文件
        File.WriteAllBytes(sourceFile, sourceData)
        Console.WriteLine($"成功将第 {ddsIndex} 个DDS修补到 {sourceFile}")
    End Sub

    Private Function LocateAllDdsFiles(fileData As Byte(), isAfbFile As Boolean) As List(Of DdsInfo)
        Dim ddsList As New List(Of DdsInfo)()
        Dim position As Integer = 0

        While position < fileData.Length - 4
            ' 检查是否是 DDS 文件头
            If fileData(position) = DDS_HEADER(0) AndAlso
               fileData(position + 1) = DDS_HEADER(1) AndAlso
               fileData(position + 2) = DDS_HEADER(2) AndAlso
               fileData(position + 3) = DDS_HEADER(3) Then

                ' 查找下一个 DDS 文件头或结束标记
                Dim nextDdsPos As Integer = FindNextDdsHeader(fileData, position + 4)
                Dim endPos As Integer = If(nextDdsPos <> -1, nextDdsPos, fileData.Length)

                ' 对于 AFB 文件，检查是否有 POF 标记
                If isAfbFile AndAlso nextDdsPos = -1 Then
                    Dim pofPos As Integer = FindPofMarker(fileData, position + 4)
                    If pofPos <> -1 Then
                        endPos = pofPos
                    End If
                End If

                ' 记录DDS信息
                ddsList.Add(New DdsInfo With {
                    .StartOffset = position,
                    .Length = endPos - position
                })

                position = endPos
            Else
                position += 1
            End If
        End While

        Return ddsList
    End Function

    Private Function FindNextDdsHeader(data As Byte(), startPos As Integer) As Integer
        For i As Integer = startPos To data.Length - 4
            If data(i) = DDS_HEADER(0) AndAlso
               data(i + 1) = DDS_HEADER(1) AndAlso
               data(i + 2) = DDS_HEADER(2) AndAlso
               data(i + 3) = DDS_HEADER(3) Then
                Return i
            End If
        Next
        Return -1
    End Function

    Private Function FindPofMarker(data As Byte(), startPos As Integer) As Integer
        ' POF 标记是 ASCII 字符串 "POF"
        For i As Integer = startPos To data.Length - 3
            If data(i) = AscW("P"c) AndAlso
               data(i + 1) = AscW("O"c) AndAlso
               data(i + 2) = AscW("F"c) Then
                Return i
            End If
        Next
        Return -1
    End Function

    Private Function ParseCommandLine(input As String) As String()
        Dim args As New List(Of String)()
        Dim currentArg As New StringBuilder()
        Dim inQuotes As Boolean = False

        For Each c As Char In input
            If c = """"c Then
                inQuotes = Not inQuotes
            ElseIf Not inQuotes AndAlso Char.IsWhiteSpace(c) Then
                If currentArg.Length > 0 Then
                    args.Add(currentArg.ToString())
                    currentArg.Clear()
                End If
            Else
                currentArg.Append(c)
            End If
        Next

        If currentArg.Length > 0 Then
            args.Add(currentArg.ToString())
        End If

        Return args.ToArray()
    End Function

    Private Structure DdsInfo
        Public StartOffset As Integer
        Public Length As Integer
    End Structure
End Module