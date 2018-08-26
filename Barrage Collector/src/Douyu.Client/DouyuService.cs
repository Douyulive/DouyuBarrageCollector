﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;
using Jack4net.Log;
using Douyu.Messsages;
using System.Web;
using Newtonsoft.Json;
using System.IO;

namespace Douyu.Client
{
    public class DouyuService
    {
        static DouyuSocket _douyuSocket;

        public static void GetBarrageServerInfo(int roomId, out IPEndPoint[] barrageServers, out int groupId)
        {
            barrageServers = null;
            groupId = 0;

            // 获取斗鱼服务器
            LogService.Info("获取斗鱼服务器");
            var servers = GetServers(roomId);

            // 连接斗鱼服务器
            Exception exception = null;
            foreach (var server in servers) {
                try {
                    LogService.InfoFormat("连接到斗鱼服务器: {0}", server.ToString());
                    exception = null;
                    _douyuSocket = new DouyuSocket();
                    _douyuSocket.Connect(server);
                } catch (Exception ex) {
                    exception = ex;
                }
                if (exception == null)
                    break;
            }
            if (exception != null)
                throw new DouyuException("连接斗鱼服务器失败!", exception);

            LogService.Info("发送登录消息");
            _douyuSocket.SendMessage(new LoginreqMessage(roomId));

            LogService.Info("获取弹幕服务器");
            barrageServers = GetBarrageServers();

            LogService.Info("获取弹幕分组");
            groupId = GetMessageGroup();

            _keepliveTimer = new Timer(
                (o) => {
                    _douyuSocket.SendMessage(new KeepliveMessage());
                }, null, 30000, 30000);
        }

        static Timer _keepliveTimer;

        static IPEndPoint[] GetServers(int roomId)
        {
            var roomPage = GetRoomPage(roomId);
            var regex = new Regex("\"server_config\":\"(?<ServerConfig>[^\"]+)\"");
            var match = regex.Match(roomPage);
            if (!match.Success)
                throw new DouyuException("没有找到斗鱼服务器列表!");

            var serverConfig = HttpUtility.UrlDecode(match.Groups["ServerConfig"].Value, Encoding.ASCII);
            var servers = new List<IPEndPoint>();
            foreach (var server in JsonConvert.DeserializeObject<dynamic>(serverConfig)) {
                servers.Add(new IPEndPoint(IPAddress.Parse(server.ip.Value), int.Parse(server.port.Value)));
            }

            if (servers.Count == 0)
                throw new DouyuException("没有找到斗鱼服务器");
            return servers.ToArray();
        }

        static string GetRoomPage(int roomId)
        {
            Stream stream = null;
            StreamReader reader = null;
            try {
                var uri = string.Format("http://www.douyu.com/{0}", roomId);
                var request = HttpWebRequest.Create(uri) as HttpWebRequest;
                request.KeepAlive = true;
                request.ProtocolVersion = HttpVersion.Version11;
                request.Method = "GET";
                request.Accept = "*/* ";
                request.UserAgent = "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/536.5 (KHTML, like Gecko) " +
                    "Chrome/19.0.1084.56 Safari/536.5";
                request.Referer = uri;
                stream = request.GetResponse().GetResponseStream();
                reader = new StreamReader(stream, Encoding.UTF8);
                var roomPage = reader.ReadToEnd();
                return roomPage;
            } finally {
                if (stream != null) stream.Close();
                if (reader != null) reader.Close();
            }
        }

        static IPEndPoint[] GetBarrageServers()
        {
            // 接收msgiplist消息
            var messageText = "";
            var stopwatch = Stopwatch.StartNew();
            do {
                if (_douyuSocket.TryGetMessage(out messageText) && messageText.Contains("type@=msgiplist"))
                    break;
                MyThread.Wait(100);
            } while (stopwatch.ElapsedMilliseconds < 20000);
            if (!messageText.Contains("type@=msgiplist"))
                throw new DouyuException("获取msgiplist消息失败!");

            // 解析弹幕服务器
            var regex = new Regex(@"ip@AA=(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})@ASport@AA=(?<port>\d+)@AS@S");
            var matches = regex.Matches(messageText);
            var barrageServers = new IPEndPoint[matches.Count];
            for (var i = 0; i < matches.Count; ++i) {
                barrageServers[i] = new IPEndPoint(
                    IPAddress.Parse(matches[i].Groups["ip"].Value),
                    int.Parse(matches[i].Groups["port"].Value));
            }
            if (barrageServers.Length == 0)
                throw new DouyuException("没有找到弹幕服务器!");
            return barrageServers;
        }

        static int GetMessageGroup()
        {
            // 接收setmsggroup消息
            var messageText = "";
            var stopwatch = Stopwatch.StartNew();
            do {
                if (_douyuSocket.TryGetMessage(out messageText) && messageText.Contains("type@=setmsggroup"))
                    break;
                MyThread.Wait(100);
            } while (stopwatch.ElapsedMilliseconds < 20000);
            if (!messageText.Contains("type@=setmsggroup"))
                throw new DouyuException("获取setmsggroup消息失败!");

            // 解析弹幕群组编号
            var regex = new Regex(@"gid@=(?<gid>\d+)");
            var match = regex.Match(messageText);
            if (!match.Success) {
                throw new DouyuException("没有找到弹幕群组编号");
            }
            return int.Parse(match.Groups["gid"].Value);
        }
    }
}
