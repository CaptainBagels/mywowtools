﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WoWPacketViewer.Parsers.Warden;

namespace WoWPacketViewer.Parsers
{
	[Parser(OpCodes.CMSG_WARDEN_DATA)]
    [Parser(OpCodes.SMSG_WARDEN_DATA)]
    internal class MSG_WARDEN_DATA : Parser
    {
        static List<CheckInfo> s_lastCheckInfo = new List<CheckInfo>();
        static Dictionary<byte, CheckType> s_checkTypes = new Dictionary<byte, CheckType>();
        static FrmWardenDebug s_wardenDebugForm;

        public MSG_WARDEN_DATA(Packet packet)
            : base(packet)
        {

        }

        public static void InitCheckTypes(Dictionary<byte, CheckType> checkTypes)
        {
            s_checkTypes = checkTypes;
        }

		public override string Parse()
		{
			switch (Packet.Code)
			{
				case OpCodes.CMSG_WARDEN_DATA:
					{
						return ParseCMSG_WARDEN_DATA();
					}

				case OpCodes.SMSG_WARDEN_DATA:
					{
						return ParseSMSG_WARDEN_DATA();
					}
				default:
					return String.Empty;
			}
		}

		private string ParseSMSG_WARDEN_DATA()
		{
			BinaryReader gr;
			gr = Packet.CreateReader();

			s_lastCheckInfo.Clear();

			var wardenOpcode = gr.ReadByte();
			AppendFormatLine("S->C Warden Opcode: {0:X2}", wardenOpcode);

			switch (wardenOpcode)
			{
				case 0x00:
					{
						var md5 = gr.ReadBytes(16); // md5
						var rc4 = gr.ReadBytes(16); // rc4 key
						var len = gr.ReadInt32();   // len
						AppendFormatLine("MD5: 0x{0}", Utility.ByteArrayToHexString(md5));
						AppendFormatLine("RC4: 0x{0}", Utility.ByteArrayToHexString(rc4));
						AppendFormatLine("Len: {0}", len);
						AppendLine();
					}
					break;
				case 0x01:
					{
						var len = gr.ReadInt16();
						var chunk = gr.ReadBytes(len);
						AppendFormatLine("Received warden module chunk, len {0}", len);
						AppendLine();
					}
					break;
				case 0x02:
					Parse_CHEAT_CHECKS(gr);
					break;
				case 0x03:
					{
						while (gr.BaseStream.Position != gr.BaseStream.Length)
						{
							var len = gr.ReadInt16();
							var checkSum = gr.ReadUInt32();
							var data = gr.ReadBytes(len);

							ValidateCheckSum(checkSum, data);

							AppendFormatLine("Len: {0}", len);
							AppendFormatLine("Checksum: 0x{0:X8}", checkSum);
							AppendFormatLine("Data: 0x{0}", Utility.ByteArrayToHexString(data));
							AppendLine();
							if (gr.BaseStream.Position != gr.BaseStream.Length)
								gr.ReadByte(); // next 0x03 opcode
						}
					}
					break;
				case 0x05:
					{
						var seed = gr.ReadBytes(16);
						AppendFormatLine("Seed: 0x{0}", Utility.ByteArrayToHexString(seed));
						AppendLine();
					}
					break;
				default:
					{
						AppendFormatLine("Unknown warden opcode {0}", wardenOpcode);
					}
					break;
			}

			CheckPacket(gr);

			return GetParsedString();
		}

		private string ParseCMSG_WARDEN_DATA()
		{
			BinaryReader gr;
			gr = Packet.CreateReader();

			var wardenOpcode = gr.ReadByte();
			AppendFormatLine("C->S Warden Opcode: {0:X2}", wardenOpcode);

			switch (wardenOpcode)
			{
				case 0x00:
					{
						AppendFormatLine("Load module failed or module is missing...");
						AppendLine();
					}
					break;
				case 0x01:
					{
						AppendFormatLine("Module loaded successfully...");
						AppendLine();
					}
					break;
				case 0x02:
					Parse_CHEAT_CHECKS_RESULTS(gr);
					break;
				case 0x04:
					{
						var hash = gr.ReadBytes(20); // SHA1 hash
						AppendFormatLine("SHA1: 0x{0}", Utility.ByteArrayToHexString(hash));
						AppendLine();
					}
					break;
				default:
					{
						AppendFormatLine("Unknown warden opcode {0}", wardenOpcode);
						AppendLine();
					}
					break;
			}

			CheckPacket(gr);

			return GetParsedString();
		}

		private void Parse_CHEAT_CHECKS_RESULTS(BinaryReader gr)
        {
            var bufLen = gr.ReadUInt16();
            var checkSum = gr.ReadUInt32();
            var result = gr.ReadBytes(bufLen);
            ValidateCheckSum(checkSum, result);
            AppendFormatLine("Cheat check result:");
            AppendFormatLine("Len: {0}", bufLen);
            AppendFormatLine("Checksum: 0x{0:X8}", checkSum);
            var reader = new BinaryReader(new MemoryStream(result), Encoding.ASCII);
            AppendFormatLine("====== CHEAT CHECKS RESULTS START ======");
            AppendLine();
            foreach (var check in s_lastCheckInfo)
            {
                switch (check.m_type)
                {
                    case CheckType.MEM_CHECK:
                        Parse_MEM_CHECK_RESULT(reader, check);
                        break;
                    case CheckType.PAGE_CHECK_A:
                    case CheckType.PAGE_CHECK_B:
                        Parse_PAGE_CHECK_RESULT(reader);
                        break;
                    case CheckType.MPQ_CHECK:
                        Parse_MPQ_CHECK_RESULT(reader);
                        break;
                    case CheckType.LUA_STR_CHECK:
                        Parse_LUA_STR_CHECK_RESULT(reader);
                        break;
                    case CheckType.DRIVER_CHECK:
                        Parse_DRIVER_CHECK_RESULT(reader);
                        break;
                    case CheckType.TIMING_CHECK:
                        Parse_TIMING_CHECK_RESULT(reader);
                        break;
                    case CheckType.PROC_CHECK:
                        Parse_PROC_CHECK_RESULT(reader);
                        break;
                    default:
                        break;
                }
            }
            AppendFormatLine("====== CHEAT CHECKS RESULTS END ======");

            s_lastCheckInfo.Clear();

            if (reader.BaseStream.Position != reader.BaseStream.Length)
                AppendFormatLine("Packet under read!");

            AppendLine();
        }

        private void Parse_PROC_CHECK_RESULT(BinaryReader reader)
        {
            var res = reader.ReadByte();
            AppendFormatLine("====== PROC_CHECK result START ======");
            AppendFormatLine("Result: 0x{0:X2}", res);
            AppendFormatLine("====== PROC_CHECK result END ======");
            AppendLine();
        }

        private void Parse_TIMING_CHECK_RESULT(BinaryReader reader)
        {
            var res = reader.ReadByte();
            var unk = reader.ReadInt32();
            AppendFormatLine("====== TIMING_CHECK result START ======");
            AppendFormatLine("Result: 0x{0:X2}", res);
            AppendFormatLine("Ticks: 0x{0:X8}", unk);
            AppendFormatLine("====== TIMING_CHECK result END ======");
            AppendLine();
        }

        private void Parse_DRIVER_CHECK_RESULT(BinaryReader reader)
        {
            var res = reader.ReadByte();
            AppendFormatLine("====== DRIVER_CHECK result START ======");
            AppendFormatLine("Result: 0x{0:X2}", res);
            AppendFormatLine("====== DRIVER_CHECK result END ======");
            AppendLine();
        }

        private void Parse_LUA_STR_CHECK_RESULT(BinaryReader reader)
        {
            var unk = reader.ReadByte();
            var len = reader.ReadByte();
            AppendFormatLine("====== LUA_STR_CHECK result START ======");
            AppendFormatLine("Result: 0x{0:X2}", unk);
            AppendFormatLine("Len: {0}", len);
            if (len > 0)
            {
                var data = reader.ReadBytes(len);
                AppendFormatLine("Data: 0x{0}", Utility.ByteArrayToHexString(data));
            }
            AppendFormatLine("====== LUA_STR_CHECK result END ======");
            AppendLine();
        }

        private void Parse_MPQ_CHECK_RESULT(BinaryReader reader)
        {
            var res = reader.ReadByte();
            var sha1 = reader.ReadBytes(20);
            AppendFormatLine("====== MPQ_CHECK result START ======");
            AppendFormatLine("Result: 0x{0:X2}", res);
            AppendFormatLine("SHA1: 0x{0}", Utility.ByteArrayToHexString(sha1));
            AppendFormatLine("====== MPQ_CHECK result END ======");
            AppendLine();
        }

        private void Parse_PAGE_CHECK_RESULT(BinaryReader reader)
        {
            var res = reader.ReadByte();
            AppendFormatLine("====== PAGE_CHECK_A_B result START ======");
            AppendFormatLine("Result: 0x{0:X2}", res);
            AppendFormatLine("====== PAGE_CHECK_A_B result END ======");
            AppendLine();
        }

        private void Parse_MEM_CHECK_RESULT(BinaryReader reader, CheckInfo check)
        {
            var res = reader.ReadByte();
            AppendFormatLine("====== MEM_CHECK result START ======");
            AppendFormatLine("Result: 0x{0:X2}", res);
            if (res == 0)
            {
                var bytes = reader.ReadBytes(check.m_length);
                AppendFormatLine("Bytes: 0x{0}", Utility.ByteArrayToHexString(bytes));
            }
            AppendFormatLine("====== MEM_CHECK result END ======");
            AppendLine();
        }

        private void Parse_CHEAT_CHECKS(BinaryReader gr)
        {
            AppendFormatLine("====== CHEAT CHECKS START ======");
            AppendLine();

            List<string> strings = new List<string>();
            strings.Add("");
            byte len;
            while ((len = gr.ReadByte()) != 0)
            {
                var strBytes = gr.ReadBytes(len);
                var str = Encoding.ASCII.GetString(strBytes);
                strings.Add(str);
            }
            var rest = gr.BaseStream.Length - gr.BaseStream.Position;
            var checks = gr.ReadBytes((int)rest);
            var reader = new BinaryReader(new MemoryStream(checks), Encoding.ASCII);

            while (reader.BaseStream.Position + 1 != reader.BaseStream.Length)
            {
                var Xor = checks[checks.Length - 1];

                var check = reader.ReadByte();
                var checkType = (byte)(check ^ Xor);

                try
                {
                    switch (s_checkTypes[check])
                    {
                        case CheckType.TIMING_CHECK:
                            Parse_TIMING_CHECK(check, checkType);
                            break;
                        case CheckType.MEM_CHECK:
                            Parse_MEM_CHECK(strings, reader, check, checkType);
                            break;
                        case CheckType.PAGE_CHECK_A:
                        case CheckType.PAGE_CHECK_B:
                            Parse_PAGE_CHECK(reader, check, checkType);
                            break;
                        case CheckType.PROC_CHECK:
                            Parse_PROC_CHECK(strings, reader, check, checkType);
                            break;
                        case CheckType.MPQ_CHECK:
                            Parse_MPQ_CHECK(strings, reader, check, checkType);
                            break;
                        case CheckType.LUA_STR_CHECK:
                            Parse_LUA_STR_CHECK(strings, reader, check, checkType);
                            break;
                        case CheckType.DRIVER_CHECK:
                            Parse_DRIVER_CHECK(strings, reader, check, checkType);
                            break;
                        default:
                            break;
                    }
                }
                catch (KeyNotFoundException ex)
                {
                    ex.ToString();

                    if (s_wardenDebugForm == null || s_wardenDebugForm.IsDisposed)
                    {
                        s_wardenDebugForm = new FrmWardenDebug();
                    }

                    var textBoxes = s_wardenDebugForm.GetTextBoxes();
                    if (textBoxes.Length != 0)
                    {
                        foreach (var tb in textBoxes)
                        {
                            if (tb.Name == "textBox1")
                            {
                                tb.Text = String.Empty;
                                foreach (var str in strings)
                                {
                                    tb.Text += str;
                                    tb.Text += "\r\n";
                                }
                                tb.Text += Utility.PrintHex(checks, 0, checks.Length);
                                continue;
                            }

                            byte val = 0;
                            if (GetByteForCheckType((CheckType)tb.TabIndex, ref val))
                                tb.Text = String.Format("{0:X2}", val);
                        }
                    }

                    if (!s_wardenDebugForm.Visible)
                    {
                        s_wardenDebugForm.Show();
                    }

                    break;
                }
            }
            AppendFormatLine("====== CHEAT CHECKS END ======");
            AppendLine();
        }

        private void Parse_DRIVER_CHECK(List<string> strings, BinaryReader reader, byte check, byte checkType)
        {
            var seed = reader.ReadUInt32();
            var sha1 = reader.ReadBytes(20);
            var stringIndex = reader.ReadByte();
            AppendFormatLine("====== DRIVER_CHECK START ======");
            AppendFormatLine("CheckType {0:X2} ({1:X2})", checkType, check);
            AppendFormatLine("Seed: 0x{0:X8}", seed);
            AppendFormatLine("SHA1: 0x{0}", Utility.ByteArrayToHexString(sha1));
            AppendFormatLine("Driver: {0}", strings[stringIndex]);
            AppendFormatLine("====== DRIVER_CHECK END ======");
            AppendLine();
            s_lastCheckInfo.Add(new CheckInfo(s_checkTypes[check], 0));
        }

        private void Parse_LUA_STR_CHECK(List<string> strings, BinaryReader reader, byte check, byte checkType)
        {
            var stringIndex = reader.ReadByte();
            AppendFormatLine("====== LUA_STR_CHECK START ======");
            AppendFormatLine("CheckType {0:X2} ({1:X2})", checkType, check);
            AppendFormatLine("String: {0}", strings[stringIndex]);
            AppendFormatLine("====== LUA_STR_CHECK END ======");
            AppendLine();
            s_lastCheckInfo.Add(new CheckInfo(s_checkTypes[check], 0));
        }

        private void Parse_MPQ_CHECK(List<string> strings, BinaryReader reader, byte check, byte checkType)
        {
            var fileNameIndex = reader.ReadByte();
            AppendFormatLine("====== MPQ_CHECK START ======");
            AppendFormatLine("CheckType {0:X2} ({1:X2})", checkType, check);
            AppendFormatLine("File: {0}", strings[fileNameIndex]);
            AppendFormatLine("====== MPQ_CHECK END ======");
            AppendLine();
            s_lastCheckInfo.Add(new CheckInfo(s_checkTypes[check], 0));
        }

        private void Parse_PROC_CHECK(List<string> strings, BinaryReader reader, byte check, byte checkType)
        {
            var seed = reader.ReadUInt32();
            var sha1 = reader.ReadBytes(20);
            var module = reader.ReadByte();
            var proc = reader.ReadByte();
            var addr = reader.ReadUInt32();
            var bytesToRead = reader.ReadByte();
            AppendFormatLine("====== PROC_CHECK START ======");
            AppendFormatLine("CheckType {0:X2} ({1:X2})", checkType, check);
            AppendFormatLine("Seed: 0x{0:X8}", seed);
            AppendFormatLine("SHA1: 0x{0}", Utility.ByteArrayToHexString(sha1));
            AppendFormatLine("Module: {0}", strings[module]);
            AppendFormatLine("Proc: {0}", strings[proc]);
            AppendFormatLine("Address: 0x{0:X8}", addr);
            AppendFormatLine("Bytes to read: {0}", bytesToRead);
            AppendFormatLine("====== PROC_CHECK END ======");
            AppendLine();
            s_lastCheckInfo.Add(new CheckInfo(s_checkTypes[check], 0));
        }

        private void Parse_PAGE_CHECK(BinaryReader reader, byte check, byte checkType)
        {
            var seed = reader.ReadUInt32();
            var sha1 = reader.ReadBytes(20);
            var addr = reader.ReadUInt32();
            var bytesToRead = reader.ReadByte();
            AppendFormatLine("====== PAGE_CHECK_A_B START ======");
            AppendFormatLine("CheckType {0:X2} ({1:X2})", checkType, check);
            AppendFormatLine("Seed: 0x{0:X8}", seed);
            AppendFormatLine("SHA1: 0x{0}", Utility.ByteArrayToHexString(sha1));
            AppendFormatLine("Address: 0x{0:X8}", addr);
            AppendFormatLine("Bytes to read: {0}", bytesToRead);
            AppendFormatLine("====== PAGE_CHECK_A_B END ======");
            AppendLine();
            s_lastCheckInfo.Add(new CheckInfo(s_checkTypes[check], 0));
        }

        private void Parse_MEM_CHECK(List<string> strings, BinaryReader reader, byte check, byte checkType)
        {
            var strIndex = reader.ReadByte(); // string index
            var offset = reader.ReadUInt32(); // offset
            var readLen = reader.ReadByte();  // bytes to read
            AppendFormatLine("====== MEM_CHECK START ======");
            AppendFormatLine("CheckType {0:X2} ({1:X2})", checkType, check);
            AppendFormatLine("Module: {0}", (strings[strIndex] == "") ? "base" : strings[strIndex]);
            AppendFormatLine("Offset: 0x{0:X8}", offset);
            AppendFormatLine("Bytes to read: {0}", readLen);
            AppendFormatLine("====== MEM_CHECK END ======");
            AppendLine();
            s_lastCheckInfo.Add(new CheckInfo(s_checkTypes[check], readLen));
        }

        private void Parse_TIMING_CHECK(byte check, byte checkType)
        {
            AppendFormatLine("====== TIMING_CHECK START ======");
            AppendFormatLine("CheckType {0:X2} ({1:X2})", checkType, check);
            AppendFormatLine("====== TIMING_CHECK END ======");
            AppendLine();
            s_lastCheckInfo.Add(new CheckInfo(s_checkTypes[check], 0));
        }

        private bool GetByteForCheckType(CheckType checkType, ref byte val)
        {
            foreach (var temp in s_checkTypes)
            {
                if (temp.Value == checkType)
                {
                    val = temp.Key;
                    return true;
                }
            }
            return false;
        }

        private void ValidateCheckSum(uint checkSum, byte[] data)
        {
            var hash = new SHA1CryptoServiceProvider().ComputeHash(data);
            var res = new uint[5];

            for (var i = 0; i < 5; ++i)
                res[i] = BitConverter.ToUInt32(hash, 4 * i);

            var newCheckSum = res[0] ^ res[1] ^ res[2] ^ res[3] ^ res[4];

            if (checkSum != newCheckSum)
                AppendFormatLine("CheckSum is not valid!");
            else
                AppendFormatLine("CheckSum is valid!");
        }
    }
}