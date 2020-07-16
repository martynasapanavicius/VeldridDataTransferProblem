#version 450

layout(set = 0, binding = 0) uniform gInfo
{
	int Width;
	int Height;
	vec2 _padding;
};

layout(std140, set = 1, binding = 0) buffer gInput
{
	vec3 Input[];
};

layout(std140, set = 2, binding = 0) buffer gOutput
{
	vec3 Output[];
};

layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

void main()
{
	int x = int(gl_GlobalInvocationID.x);
	int y = int(gl_GlobalInvocationID.y);
	if (x >= Width || y >= Height)
	{
		return;
	}

	int index = Width * y + x;
    vec3 color = Input[index];

    Output[index] = vec3(1 - color.r, 1 - color.g, 1 - color.b);
}
