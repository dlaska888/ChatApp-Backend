﻿namespace WebService.Exceptions;

public class NotFoundException(string message) : Exception(message);