// Диагностический приёмник для потока ПН из приведённого пользователем лога.
// Замените этим файлом Program.cs в вашем консольном проекте.
// Требуется System.IO.Ports (уже используется в исходной программе).
//
// Кодирование FF восстановлено по 16 пакетам: все они проходят CRC.
// Это не универсальная реализация протокола protocol_tenso-m_1.pdf:
// в PDF описано другое экранирование. Другие прошивки требуют проверки.
// V1...V6 — позиции значений в пакете, привязка к X1...X12 не установлена.
// Значения выводятся как беззнаковые 24-битные коды, не килограммы.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;

class Program
{
    static void Main(string[] args)
    {
        // Необязательно: передайте другое имя порта первым аргументом.
        string portName = args.Length > 0 ? args[0] : "COM12";
        Console.OutputEncoding = Encoding.UTF8;

        using var port = new SerialPort(
            portName, 115200, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 200,
            WriteTimeout = 1000
        };

        var receiver = new PnReceiver();
        byte[] buffer = new byte[4096];
        var timer = Stopwatch.StartNew();
        long lastPrint = -250;
        long lastValid = -1;
        long good = 0;
        long bad = 0;
        int[] latest = null;

        receiver.FrameReceived += values =>
        {
            latest = values;
            lastValid = timer.ElapsedMilliseconds;
            good++;
        };
        receiver.FrameRejected += reason =>
        {
            bad++;
            // Чтобы ошибки не заполняли консоль, печатаем первые три.
            if (bad <= 3)
                Console.WriteLine("Пакет отклонён: " + reason);
        };

        try
        {
            port.Open();

            byte[] command =
{
    0xFF, 0x03, 0xFD, 0xF4, 0xFF
};
            byte[] comman2 =
{
    0xFF, 0x04, 0xCE, 0x0C, 0x05,0xFF
};

            port.Write(command, 0, command.Length);
            Console.WriteLine(portName + " открыт: 115200, 8N1.");
            Console.WriteLine("V1...V6 — коды по позициям в пакете. Enter — выход.");
            Thread.Sleep(1000);
            port.Write(comman2, 0, comman2.Length);
            Console.WriteLine(portName + " открыт: 115200, 8N1.");
            Console.WriteLine("V1...V6 — коды по позициям в пакете. Enter — выход.");
            Thread.Sleep(1000);
            //port.Write(command, 0, command.Length);
            //Console.WriteLine(portName + " открыт: 115200, 8N1.");
            //Console.WriteLine("V1...V6 — коды по позициям в пакете. Enter — выход.");
            //Thread.Sleep(1000);
            //port.Write(command, 0, command.Length);
            //Console.WriteLine(portName + " открыт: 115200, 8N1.");
            //Console.WriteLine("V1...V6 — коды по позициям в пакете. Enter — выход.");
            //Thread.Sleep(1000);
            //port.Write(command, 0, command.Length);
            //Console.WriteLine(portName + " открыт: 115200, 8N1.");
            //Console.WriteLine("V1...V6 — коды по позициям в пакете. Enter — выход.");
            //Thread.Sleep(1000);
            //port.Write(command, 0, command.Length);
            //Console.WriteLine(portName + " открыт: 115200, 8N1.");
            //Console.WriteLine("V1...V6 — коды по позициям в пакете. Enter — выход.");
            //Thread.Sleep(1000);
            //port.Write(command, 0, command.Length);
            //Console.WriteLine(portName + " открыт: 115200, 8N1.");
            //Console.WriteLine("V1...V6 — коды по позициям в пакете. Enter — выход.");
            //Thread.Sleep(1000);
            while (true)
            {
                if (Console.KeyAvailable &&
                    Console.ReadKey(true).Key == ConsoleKey.Enter)
                    break;

                try
                {
                    int read = port.Read(buffer, 0, buffer.Length);
                    receiver.Feed(buffer, read);
                }
                catch (TimeoutException)
                {
                    // Пауза в потоке не завершает программу.
                }

                long now = timer.ElapsedMilliseconds;
                if (now - lastPrint < 250)
                    continue;
                lastPrint = now;

                if (latest == null)
                {
                    Console.WriteLine("Ожидание корректного пакета; ошибок=" + bad);
                    continue;
                }

                long age = now - lastValid;
                if (age > 2000)
                {
                    Console.WriteLine("НЕТ СВЕЖИХ ДАННЫХ: " + age +
                        " мс; принято=" + good + "; ошибок=" + bad);
                    continue;
                }

                var line = new StringBuilder();
                for (int i = 0; i < latest.Length; i++)
                {
                    if (i > 0) line.Append(" | ");
                    line.Append("V").Append(i + 1).Append('=').Append(latest[i]);
                }
                line.Append(" | CRC OK=").Append(good);
                line.Append(" | ошибок=").Append(bad);
                line.Append(" | возраст=").Append(age).Append(" мс");
                Console.WriteLine(line);
            }
        }
        catch (Exception ex) when (
            ex is IOException || ex is UnauthorizedAccessException ||
            ex is ArgumentException || ex is InvalidOperationException)
        {
            Console.Error.WriteLine("Ошибка COM-порта: " + ex.Message);
        }
    }
}

sealed class PnReceiver
{
    // Только наблюдаемый формат: служебный байт + CC + 6*3 + CRC = 21.
    // Пакеты иной длины отклоняются, чтобы не угадывать их назначение.
    private const int EncodedLength = 21;
    private readonly List<byte> frame = new List<byte>(EncodedLength);
    private bool synchronized;

    public event Action<int[]> FrameReceived;
    public event Action<string> FrameRejected;

    // Feed можно вызывать с любой длиной блока; один Read не равен пакету.
    public void Feed(byte[] bytes, int count)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        if (count < 0 || count > bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        for (int i = 0; i < count; i++)
        {
            byte b = bytes[i];
            if (b == 0xFF)
            {
                if (synchronized && frame.Count > 0)
                    ProcessFrame(frame.ToArray());
                frame.Clear();
                synchronized = true;
                continue;
            }

            if (!synchronized) continue;
            if (frame.Count >= EncodedLength)
            {
                Reject("длина больше 21 байта; повторная синхронизация");
                frame.Clear();
                synchronized = false;
                continue;
            }
            frame.Add(b);
        }
    }

    private void ProcessFrame(byte[] encoded)
    {
        if (encoded.Length != EncodedLength)
        {
            Reject("неожиданная длина: " + encoded.Length);
            return;
        }

        // Первый байт — расстояние до следующей замены FF либо конца.
        // В позиции замены хранится расстояние до следующей замены.
        // Сначала читаем расстояние, затем восстанавливаем FF.
        int position = 0;
        while (true)
        {
            int distance = encoded[position];
            if (distance == 0 || position + distance > encoded.Length)
            {
                Reject("некорректная цепочка восстановления FF");
                return;
            }
            int next = position + distance;
            if (position > 0) encoded[position] = 0xFF;
            if (next == encoded.Length) break;
            position = next;
        }

        // Исключаем начальный служебный байт, оставляем CC, данные, CRC.
        byte[] payload = new byte[encoded.Length - 1];
        Array.Copy(encoded, 1, payload, 0, payload.Length);
        if (payload[0] != 0xCC)
        {
            Reject("неожиданный код операции: " + payload[0].ToString("X2"));
            return;
        }
        if (Crc69(payload) != 0)
        {
            Reject("CRC не совпадает");
            return;
        }

        int[] values = new int[6];
        for (int i = 0; i < values.Length; i++)
        {
            int offset = 1 + i * 3;
            values[i] = payload[offset]
                | (payload[offset + 1] << 8)
                | (payload[offset + 2] << 16);
        }
        FrameReceived?.Invoke(values);
    }

    // Буквальный алгоритм CRCMaker со стр. 2 приложенного протокола.
    // При проверке обрабатываем CC, восстановленные данные и сам CRC.
    // Нулевой остаток означает успешную проверку.
    private static byte Crc69(byte[] bytes)
    {
        int crc = 0;
        foreach (byte b in bytes)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                bool carry = (crc & 0x80) != 0;
                crc = ((crc << 1) | ((b >> bit) & 1)) & 0xFF;
                if (carry) crc ^= 0x69;
            }
        }
        return (byte)crc;
    }

    private void Reject(string reason)
    {
        FrameRejected?.Invoke(reason);
    }
}
