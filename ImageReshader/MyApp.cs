using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Maths;
using System.Drawing;
using StbImageSharp;
using System.Numerics;

namespace ImageReshader
{
    public class MyApp
    {
        #region Variables
        /// <summary>
        /// The Window. Everything happens in here.
        /// </summary>
        IWindow window;
        static GL Gl;

        /// <summary>
        /// Vertex Array Objects
        /// </summary>
        private static uint vao;
        /// <summary>
        /// Vertex Buffer Objects
        /// </summary>
        private static uint vbo;
        /// <summary>
        /// Element Buffer Objects
        /// </summary>
        private static uint ebo;

        /// <summary>
        /// Frame Buffer Objects
        /// </summary>
        private static uint fbo;

        private static uint program;

        private static uint texture;
        private static uint depthTexture;

        private static Vector2 LastMousePosition;
        private static Vector2D<int> currentTextureSize;
        private static Vector2D<int> screenSize;

        #region Shaders
        //TODO: Find out whether or not I can keep this as-is.
        const string vertexCode = @"
        #version 330 core
        
        layout (location = 0) in vec3 aPosition;

        // On top of our aPosition attribute, we now create an aTexCoords attribute for our texture coordinates.
        layout (location = 1) in vec2 aTexCoords;

        // Likewise, we also assign an out attribute to go into the fragment shader.
        out vec2 frag_texCoords;
        
        void main()
        {
            gl_Position = vec4(aPosition, 1.0);

            // This basic vertex shader does no additional processing of texture coordinates, so we can pass them
            // straight to the fragment shader.
            frag_texCoords = aTexCoords;
        }";

        //TODO: Find or write a shader that displays a texture.
        const string fragmentCode = @"
        #version 330 core

        // This in attribute corresponds to the out attribute we defined in the vertex shader.
        in vec2 frag_texCoords;
        
        out vec4 out_color;

        // Now we define a uniform value!
        // A uniform in OpenGL is a value that can be changed outside of the shader by modifying its value.
        // A sampler2D contains both a texture and information on how to sample it.
        // Sampling a texture is basically calculating the color of a pixel on a texture at any given point.
        uniform sampler2D uTexture;
        
        void main()
        {
            // We use GLSL's texture function to sample from the texture at the given input texture coordinates.
            out_color = texture(uTexture, frag_texCoords);
        }";

        #endregion

        #endregion

        #region Construction
        /// <summary>
        /// Constructor for the MyGame class. No arguments, just handles configuring our window.
        /// </summary>
        public MyApp()
        {
            window = Window.Create(WindowOptions.Default with
            {
                Size = new Vector2D<int>(1024, 768),
                Title = "Image Reshader",
                WindowBorder = WindowBorder.Fixed,
                PreferredDepthBufferBits = 256
            });

            window.Render += OnRender;
            window.Update += OnUpdate;
            window.Load += OnLoad;
            window.FramebufferResize += OnResize;
            window.Initialize();
            window.FileDrop += OnFileDrop;
        }

        #region input

        /// <summary>
        /// Handles events on keypress.
        /// </summary>
        /// <param name="keyboard">Which keyboard triggerd the keypress?</param>
        /// <param name="key">What key was pressed?</param>
        /// <param name="arg3">Internal integer ID for the key that was pressed.</param>
        private void KeyDown(IKeyboard keyboard, Key key, int arg3)
        {
            Console.WriteLine(key);

            if (key == Key.Escape)
                window.Close();

            // On Tab key, toggle between preview size and full image size
            if (key == Key.Tab)
            {
                if (window.Size == currentTextureSize)
                    ResizeWindow(currentTextureSize, true);
                else
                    ResizeWindow(currentTextureSize, false);
            }
        }

        /// <summary>
        /// Keeps track of our mouse position
        /// </summary>
        /// <param name="mouse"></param>
        /// <param name="position"></param>
        private static unsafe void OnMouseMove(IMouse mouse, Vector2 position)
        {
            const float lookSensitivity = 0.1f;
            if (LastMousePosition == default) { LastMousePosition = position; }
            else
            {
                var xOffset = (position.X - LastMousePosition.X) * lookSensitivity;
                var yOffset = (position.Y - LastMousePosition.Y) * lookSensitivity;
                LastMousePosition = position;

                //Console.WriteLine(position);
            }
        }

        /// <summary>
        /// Method called when a file is dropped onto the window.
        /// </summary>
        /// <param name="obj"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void OnFileDrop(string[] obj)
        {
            // Return if we've dropped more than one file, or if the file is not a png.
            if (obj.Length > 1)
            {
                Console.WriteLine("Only one thing at a time please.");
                return;
            }
            else if (!obj[0].Contains("png"))
            {
                Console.WriteLine("Only PNG files are supported.");
                return;
            }

            // If we're dragging our item into the top half of the window, its RGB. Bottom half, depth.
            if(LastMousePosition.Y < window.Size.Y / 2)
            {
                Gl.ActiveTexture(TextureUnit.Texture0);
                Gl.BindTexture(TextureTarget.Texture2D, texture);
                LoadTexture(obj[0]);

                ResizeWindow(currentTextureSize, true);
            }
            else
            {
                Gl.ActiveTexture(TextureUnit.Texture1);
                Gl.BindTexture(TextureTarget.Texture2D, depthTexture);
                LoadDepthTexture(obj[0]);
            }

            for (int i  = 0; i < obj.Length; i++)
            {
                Console.WriteLine(obj[i]);
            }
        }

        #endregion

        /// <summary>
        /// Run our window on program start.
        /// </summary>
        public void Start()
        {
            window.Run();
        }

        /// <summary>
        /// Method to do some initial setup when our window loads.
        /// </summary>
        private unsafe void OnLoad()
        {
            Gl = window.CreateOpenGL();

            Gl.ClearColor(Color.CornflowerBlue);

            IInputContext input = window.CreateInput();

            for (int i = 0; i < input.Keyboards.Count; i++)
                input.Keyboards[i].KeyDown += KeyDown;

            for (int i = 0; i < input.Mice.Count; i++)
            {
                input.Mice[i].Cursor.CursorMode = CursorMode.Normal;
                input.Mice[i].MouseMove += OnMouseMove;
            }

            screenSize = (Vector2D<int>)window.Monitor.VideoMode.Resolution;

            vao = Gl.GenVertexArray();
            Gl.BindVertexArray(vao);

            vbo = Gl.GenBuffer();
            Gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

            float[] vertices =
            {
              // aPosition--------   aTexCoords
                 1.0f,  1.0f, 0.0f,  1.0f, 0.0f,
                 1.0f, -1.0f, 0.0f,  1.0f, 1.0f,
                -1.0f, -1.0f, 0.0f,  0.0f, 1.0f,
                -1.0f,  1.0f, 0.0f,  0.0f, 0.0f
            };

            fixed (float* buf = vertices)
                Gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), buf, BufferUsageARB.StaticDraw);

            uint[] indices =
            {
                0u, 1u, 3u,
                1u, 2u, 3u
            };

            ebo = Gl.GenBuffer();
            Gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);

            fixed (uint* buf = indices)
                Gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), buf, BufferUsageARB.StaticDraw);

            fbo = Gl.GenFramebuffer();
            Gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);

            // Create texture
            texture = Gl.GenTexture();
            Gl.ActiveTexture(TextureUnit.Texture0);
            Gl.BindTexture(TextureTarget.Texture2D, texture);
            LoadTexture("Images/Guide.png");

            depthTexture = Gl.GenTexture();
            Gl.ActiveTexture(TextureUnit.Texture1);
            Gl.BindTexture(TextureTarget.Texture2D, depthTexture);
            LoadDepthTexture("Images/depthtest.png");

            if (Gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == GLEnum.FramebufferComplete)
            {
                Console.WriteLine("Frame buffer set up correctly.");
            }
            else
            {
                Console.WriteLine("Frame buffer setup unsuccessful");
            }

            LoadShaders();

            CompileShaders();

            // Our stride constant. The stride must be in bytes, so we take the first attribute (a vec3), multiply it
            // by the size in bytes of a float, and then take our second attribute (a vec2), and do the same
            const uint stride = (3 * sizeof(float)) + (2 * sizeof(float));

            // Enable the "aPosition" attribute in our vertex  array, providing its size and stride
            const uint positionLoc = 0;
            Gl.EnableVertexAttribArray(positionLoc);
            Gl.VertexAttribPointer(positionLoc, 3, VertexAttribPointerType.Float, false, stride, (void*) 0);

            const uint textureLoc = 1;
            Gl.EnableVertexAttribArray(textureLoc);
            Gl.VertexAttribPointer(textureLoc, 2, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

            Gl.BindVertexArray(0);
            Gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            Gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
            Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            Gl.DeleteFramebuffers(0, fbo);
        }

        /// <summary>
        /// Given a string path, load in a png image for use as the main texture on the quad.
        /// </summary>
        /// <param name="filePath">Path to the file we need to load.</param>
        private unsafe void LoadTexture(string filePath)
        {
            ImageResult result = ImageResult.FromMemory(File.ReadAllBytes(filePath), ColorComponents.RedGreenBlueAlpha);

            fixed (byte* ptr = result.Data)
            {
                Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint) result.Width, (uint) result.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }

            currentTextureSize = new Vector2D<int>((int)result.Width, (int)result.Height);

            Gl.TextureParameter(texture, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            Gl.TextureParameter(texture, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            Gl.TextureParameter(texture, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            Gl.TextureParameter(texture, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            Gl.GenerateMipmap(TextureTarget.Texture2D);

            Gl.BindTexture(TextureTarget.Texture2D, 0);

            // Get our texture uniform, and set it to 0.
            int location = Gl.GetUniformLocation(program, "uTexture");
            Gl.Uniform1(location, 0);
        }

        /// <summary>
        /// Given a string file path, load in a png image for use as the scene's depth buffer.
        /// </summary>
        /// <param name="filepath">Path to the file we need to load.</param>
        private unsafe void LoadDepthTexture(string filePath)
        {
            ImageResult result = ImageResult.FromMemory(File.ReadAllBytes(filePath), ColorComponents.RedGreenBlueAlpha);

            fixed (byte* ptr = result.Data)
            {
                Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)result.Width, (uint)result.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }

            Gl.TextureParameter(depthTexture, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            Gl.TextureParameter(depthTexture, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            Gl.TextureParameter(depthTexture, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            Gl.TextureParameter(depthTexture, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            Gl.GenerateMipmap(TextureTarget.Texture2D);

            Gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, GLEnum.Texture1, depthTexture, 0);

            Gl.BindTexture(TextureTarget.Texture2D, 0);

            int location = Gl.GetUniformLocation(program, "uTexture");
            Gl.Uniform1(location, 1);
        }

        /// <summary>
        /// Changes the size of the window to a specified value, with two alternate methods depending on the bool.
        /// </summary>
        /// <param name="size">The new window size.</param>
        /// <param name="isPreviewMode">If false, use the given size as-is. If true, compare the size of the image to the size of the screen.
        /// When image is smaller than the screen, display the image full-size. When image is larger, display the image at 80% of the screen size.</param>
        private void ResizeWindow(Vector2D<int> size, bool isPreviewMode)
        {
            if(!isPreviewMode)
                window.Size = size;
            else
            {
                if (screenSize.X <= size.X)
                {
                    float bufferAmount = screenSize.Y * 0.8f;
                    float widthTemp = ((float)screenSize.Y / (float)screenSize.X) * (screenSize.X - (int)bufferAmount);
                    window.Size = new Vector2D<int>(screenSize.X - (int)bufferAmount, (int)widthTemp);
                }
                else
                    window.Size = size;
            }
        }

        #endregion

        #region Shader compilation

        /// <summary>
        /// Loads shaders from external files.
        /// Currently unused.
        /// </summary>
        private void LoadShaders()
        {
            // Load shaders from external files; not currently needed
        }

        /// <summary>
        /// Compiles a vertex and fragment shader. We only need one of each.
        /// </summary>
        /// <exception cref="Exception">Various exceptions can be thrown based on success of shader compilation and linking.</exception>
        private void CompileShaders()
        {
            // Create our vertex shader, and give it our vertex shader source code
            uint vertexShader = Gl.CreateShader(ShaderType.VertexShader);
            Gl.ShaderSource(vertexShader, vertexCode);

            // Attempt to compile the shader
            Gl.CompileShader(vertexShader);

            // Check to make sure that the shader has successfully compiled
            Gl.GetShader(vertexShader, ShaderParameterName.CompileStatus, out int vStatus);
            if (vStatus != (int)GLEnum.True)
                throw new Exception("Vertex shader filed to compile: " + Gl.GetShaderInfoLog(vertexShader));

            // Repeat this process for our fragment shader
            uint fragmentShader = Gl.CreateShader(ShaderType.FragmentShader);
            Gl.ShaderSource(fragmentShader, fragmentCode);

            Gl.CompileShader(fragmentShader);

            Gl.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out int fStatus);
            if (fStatus != (int)GLEnum.True)
                throw new Exception("Fragment shader failed to compile: " + Gl.GetShaderInfoLog(fragmentShader));

            // Create our shader program, and attach the vertex & fragment shaders
            program = Gl.CreateProgram();

            Gl.AttachShader(program, vertexShader);
            Gl.AttachShader(program, fragmentShader);

            // Attempt to link the program together
            Gl.LinkProgram(program);

            Gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int lStatus);
            if (lStatus != (int)GLEnum.True)
                throw new Exception("Program failed to link: " + Gl.GetProgramInfoLog(program));

            Gl.DetachShader(program, vertexShader);
            Gl.DetachShader(program, fragmentShader);
            Gl.DeleteShader(vertexShader);
            Gl.DeleteShader(fragmentShader);
        }

        #endregion

        #region Runtime

        /// <summary>
        /// Update method.
        /// </summary>
        /// <param name="deltaTime">Time since last frame.</param>
        private static void OnUpdate(double deltaTime)
        {
            
        }

        /// <summary>
        /// Render method.
        /// </summary>
        /// <param name="deltaTime">Time since last frame.</param>
        private static unsafe void OnRender(double deltaTime)
        {
            
            Gl.Clear(ClearBufferMask.ColorBufferBit);
            Gl.Clear(ClearBufferMask.DepthBufferBit);

            Gl.BindVertexArray(vao);
            Gl.UseProgram(program);

            Gl.ActiveTexture(TextureUnit.Texture0);
            Gl.BindTexture(TextureTarget.Texture2D, texture);

            Gl.ActiveTexture(TextureUnit.Texture1);
            Gl.BindTexture(GLEnum.DepthStencil, depthTexture);

            Gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*)0);
        }

        private static void OnResize(Vector2D<int> size)
        {
            // Ensure that the OpenGL viewport stays the same as our screen size.
            Gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);
        }

        #endregion
    }
}
