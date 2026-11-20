#include "record_copy.h"

struct Record4 { unsigned int data[1]; };
struct Record16 { unsigned int data[4]; };

ASSIGN_RANGE(r10df00, Record4)
ASSIGN_RANGE(r4943d0, Record4)
ASSIGN_RANGE(r49c010, Record4)
ASSIGN_RANGE(r4be7f0, Record4)
ASSIGN_RANGE(r21cba0, Record16)
ASSIGN_RANGE(r21cbe0, Record16)
ASSIGN_RANGE(r21cc20, Record16)
ASSIGN_RANGE(r21cc60, Record16)
ASSIGN_RANGE(r21cca0, Record16)
COPY_BACKWARD(r21ccf0, Record16)
COPY_BACKWARD(r21cd30, Record16)
COPY_BACKWARD(r21cd70, Record16)
COPY_BACKWARD(r21cdb0, Record16)
COPY_BACKWARD(r21cdf0, Record16)
