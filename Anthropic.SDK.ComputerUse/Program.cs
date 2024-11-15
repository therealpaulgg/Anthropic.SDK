﻿using System.Text.Json.Nodes;
using Anthropic.SDK.Common;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Message = Anthropic.SDK.Messaging.Message;
using Anthropic.SDK.ComputerUse.ScreenCapture;
using Anthropic.SDK.ComputerUse.Inputs;
using Anthropic.SDK.ComputerUse.Scaling;

namespace Anthropic.SDK.ComputerUse
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            IScreenCapturer capturer = new WindowsScreenCapturer();
            var screenCap = DownscaleScreenshot(capturer.CaptureScreen(1));

            var displayNumber = 2;

            var client = new AnthropicClient();

            var messages = new List<Message>();
            messages.Add(new Message()
            {
                Role = RoleType.User,
                Content = new List<ContentBase>()
                {
                    new TextContent()
                    {
                        Text = """
                               Find Flights between ATL and NYC using a Google Search. 
                               Google Chrome is already open and maximized in the appropriate window. 
                               Use that instance of Google Chrome. 
                               It is not focused, so you'll need to click one extra time to focus on the window first. 
                               Use keyboard shortcuts to access the search bar and complete your search once you've focused on the window.
                               """
                    }
                }
            });
            
            var tools = new List<Common.Tool>()
            {
                new Function("computer", "computer_20241022",new Dictionary<string, object>()
                {
                    {"display_width_px", 1920 },
                    {"display_height_px", 1080 },
                    {"display_number", displayNumber }
                })
            };
            var parameters = new MessageParameters()
            {
                Messages = messages,
                MaxTokens = 2048,
                Model = AnthropicModels.Claude35Sonnet,
                Stream = false,
                Temperature = 0m,
                Tools = tools,
                System = new List<SystemMessage>()
                {
                    new SystemMessage($""""
                                      <SYSTEM_CAPABILITY>
                                      * You are utilising a Windows machine with internet access and an open Google Chrome Window.
                                      * When viewing a page it can be helpful to zoom out so that you can see everything on the page.  Either that, or make sure you scroll down to see everything before deciding something isn't available.
                                      * When using your computer function calls, they take a while to run and send back to you.  Where possible/feasible, try to chain multiple of these calls all into one function calls request.
                                      * The current date is {DateTime.Today.ToShortDateString()}.
                                      </SYSTEM_CAPABILITY>
                                      
                                      """
                                      """")
                }
            };
            var res = await client.Messages.GetClaudeMessageAsync(parameters);

            messages.Add(res.Message);

            var toolUse = res.Content.OfType<ToolUseContent>().First();
            var id = toolUse.Id;
            var param1 = toolUse.Input["action"].ToString();
            switch (param1)
            {
                case "screenshot":
                    messages.Add(new Message()
                    {
                        Role = RoleType.User,
                        Content = new List<ContentBase>()
                        {
                            new ToolResultContent()
                            {
                                ToolUseId = id,
                                Content =new List<ContentBase>() { new ImageContent()
                                {
                                    Source = new ImageSource() { 
                                        Data = screenCap,
                                        MediaType = "image/jpeg"
                                    }
                                } }
                            }
                        }
                    });
                    break;
            }

            var workingResult = await client.Messages.GetClaudeMessageAsync(parameters);
            messages.Add(workingResult.Message);

            var toolUse2 = workingResult.Content.OfType<ToolUseContent>().ToList();
            var cb = new List<ContentBase>();
            foreach (var tool in toolUse2)
            {
                var action = tool.Input["action"].ToString();
                var text = tool.Input["text"]?.ToString();
                var coordinate = tool.Input["coordinate"] as JsonArray;

                TakeAction(action, text,
                    coordinate == null ? null : new Tuple<int, int>(Convert.ToInt32(coordinate[0].ToString()),
                        Convert.ToInt32(coordinate[1].ToString())), 1);
                await Task.Delay(1000);
                cb.Add(new ToolResultContent()
                {
                    ToolUseId = tool.Id,
                    Content = new List<ContentBase>()
                    {
                        new TextContent()
                        {
                            Text = "Action completed"
                        }
                    }
                });
            }

            cb.Add(new TextContent()
            {
                Text = "How much does the cheapest flight from ATL to NYC you see on the page without scrolling cost?"
            });

            messages.Add(new Message()
            {
                Role = RoleType.User,
                Content = cb
            });
            await Task.Delay(5000);
            var workingResult2 = await client.Messages.GetClaudeMessageAsync(parameters);

            messages.Add(workingResult2.Message);

            var toolUse3 = workingResult2.Content.OfType<ToolUseContent>().First();
            var id2 = toolUse3.Id;
            var param2 = toolUse3.Input["action"].ToString();
            switch (param2)
            {
                case "screenshot":
                    messages.Add(new Message()
                    {
                        Role = RoleType.User,
                        Content = new List<ContentBase>()
                        {
                            new ToolResultContent()
                            {
                                ToolUseId = id2,
                                Content =new List<ContentBase>() { new ImageContent()
                                {
                                    Source = new ImageSource() {
                                        Data = DownscaleScreenshot(capturer.CaptureScreen(1)),
                                        MediaType = "image/jpeg"
                                    }
                                } }
                            }
                        }
                    });
                    break;
            }

            var finalResult = await client.Messages.GetClaudeMessageAsync(parameters);
            messages.Add(finalResult.Message);
            Console.WriteLine(finalResult.FirstMessage.ToString());
            Console.ReadLine();
        }

        public static void TakeAction(string action, string? text, Tuple<int, int>? coordinate, int monitorIndex)
        {
            var coordScaler = new CoordinateScaler(true, 1920, 1080);
            
            switch (action)
            {
                case "left_click":
                    MouseController.LeftClick();
                    break;
                case "type":
                    KeyboardSimulator.SimulateTextInput(text);
                    break;
                case "key":
                    KeyboardSimulator.SimulateKeyCombination(text);
                    break;
                case "mouse_move":
                    var scaledCoord = coordScaler.ScaleCoordinates(ScalingSource.API, coordinate.Item1, coordinate.Item2);
                    MouseController.SetCursorPositionOnMonitor(monitorIndex, scaledCoord.Item1, scaledCoord.Item2);
                    break;
                default:
                    throw new ToolError($"Action {action} is not supported");
            }
        }


        public static string DownscaleScreenshot(byte[] screenshot)
        {
            // Convert Bitmap to MemoryStream
            using var memoryStream = new MemoryStream(screenshot);
            
            memoryStream.Position = 0; // Reset stream position

            // Load the image into ImageSharp
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(memoryStream);
            // Resize the image to 1280x780
            image.Mutate(x => x.Resize(1280, 780));

            // Save the image
            using var ms = new MemoryStream();
            image.Save(ms, new JpegEncoder()); // Format is inferred from the file extension
            ms.Position = 0; // Reset stream position
            //convert to byte 64 string
            byte[] imageBytes = ms.ToArray();
            return Convert.ToBase64String(imageBytes);
        }
    }


    
    

    


    


    

    
}
