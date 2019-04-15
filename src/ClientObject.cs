using System;
using System.Net.Sockets;
using System.Text;

/*
<add name="DefaultConnection"
         connectionString="Data Source=.\SQLEXPRESS;Initial Catalog=NuclearDb;Integrated Security=True"
        providerName="System.Data.SqlClient"/>
*/

namespace Server.src
{
    class ClientObject : ServerObject
    {
        protected internal string Id { get; private set; }
        protected internal NetworkStream Stream { get; private set; }
        protected internal string located { get; private set; }
        protected internal string userName { get; private set; }
        string message;
        string[] decode = null;
        TcpClient client;
        ServerObject server; // объект сервера

        public ClientObject(TcpClient tcpClient, ServerObject serverObject)
        {
            client = tcpClient;
            server = serverObject;
            Stream = client.GetStream();
            message = GetMessage();
            decode = message.Split(' ');
            if(decode[0] == "11")
            {
                Id = Guid.NewGuid().ToString();
                serverObject.AddConnection(this);
                located = decode[1];
            }
        }

        public void Process()
        {
            if(located != null)
            {
                try
                {
                    decode = message.Split(' ');
                    userName = decode[2];
                    message = userName + " присоединился";
                    server.BroadcastMessage(CommandDecryption(decode), this.Id, located);
                    Console.WriteLine(message + " к комнате " + located);
                    while (true)
                    {
                        try
                        {
                            message = GetMessage();
                            string[] decode = message.Split(' ');
                            Console.WriteLine(DateTime.Now.ToShortTimeString() + ":OK: " + message);
                            if (decode[1] == "d")
                            {
                                server.BroadcastMessage(message, this.Id, located);
                                message = "15 " + decode[1] + " " + located + " " + userName;
                                decode = message.Split(' ');
                                CommandDecryption(decode);
                            }
                            else if(decode[1] == "t-" || decode[1] == "t+")
                            {
                                message = "15 " + decode[1] + " " + located + " " + userName;
                                decode = message.Split(' ');
                                CommandDecryption(decode);
                                message = "16 " + decode[1] + " " + located;
                                decode = message.Split(' ');
                                message = CommandDecryption(decode);
                                decode = message.Split(' ');
                                server.BroadcastMessage(message, this.Id, located);
                            }
                            else
                                server.BroadcastMessage(message, this.Id, located);
                        }
                        catch
                        {
                            Console.WriteLine(userName + ": покинул комнату " + located);
                            server.BroadcastMessage("200 " + userName + ": покинул комнату " + located, this.Id, located);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message + " Error 1");
                }
                finally
                {
                    server.RemoveConnection(this.Id);
                    Close();
                }
            }
            else
            {
                try
                {
                    string[] decode = message.Split(' ');
                    string answer = CommandDecryption(decode);// обрабатываем команду
                    if (answer != "Ошибка"
                        && answer != "Данный логин уже зарегистрирован"
                        && answer != "Данный лог уже активен"
                        && answer != "Пароль неверный"
                        && answer != "Данный логин незарегистрирован"
                        && answer != "Комната с таким именем уже существует"
                        && answer != "Комната заполнена"
                        && answer != "Недоступен по уровню"
                        && answer != "Ошибка: Case(3)"
                        && answer != "Ошибка: Case(4)"
                        && answer != "Данных о пользователе нет"
                        && answer != "Данное название недоступно"
                        && answer != "нет"
                        && answer != "вход"
                        && answer != "отправить")
                    {
                        Console.WriteLine(DateTime.Now.ToShortTimeString() + ":OK: " + answer);
                        message = answer;
                        byte[] data = Encoding.Unicode.GetBytes(message);
                        Stream.Write(data, 0, data.Length);
                    }
                    else if(answer == "вход" || answer == "отправить")
                    {
                        Console.WriteLine(DateTime.Now.ToShortTimeString() + ":OK:" + answer + " " + message);
                        byte[] data = Encoding.Unicode.GetBytes(answer);
                        Stream.Write(data, 0, data.Length);
                    }
                    else
                    {
                        Console.WriteLine(DateTime.Now.ToShortTimeString() + ":ERROR:" + answer);
                        byte[] data = Encoding.Unicode.GetBytes(answer);
                        Stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message + " Error 2");
                }
                finally
                {
                    Close();
                }
            }
        }

        // чтение входящего сообщения и преобразование в строку
        private string GetMessage()
        {
            byte[] data = new byte[64];
            StringBuilder builder = new StringBuilder();
            int bytes = 0;
            do
            {
                bytes = Stream.Read(data, 0, data.Length);
                builder.Append(Encoding.Unicode.GetString(data, 0, bytes));
            }
            while (Stream.DataAvailable);

            return builder.ToString();
        }

        // закрытие подключения
        protected internal void Close()
        {
            if (Stream != null)
                Stream.Close();
            if (client != null)
                client.Close();
        }
    }
}
