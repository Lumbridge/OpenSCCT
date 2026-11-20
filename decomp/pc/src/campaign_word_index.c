struct Array {
    const unsigned *values;
    int count, capacity;
};
int __fastcall find_word(const struct Array *array, unsigned unused_edx, const unsigned *value)
{
    int i;
    for (i = 0; i < array->count; ++i)
        if (array->values[i] == *value)
            return i;
    return -1;
}
