"""Regression: raising a paw must not redraw the rump, tail or lower legs."""
from pathlib import Path
import unittest
from PIL import Image
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
class StableRumpTests(unittest.TestCase):
    def test_greeting_keeps_master_rump_and_tail(self):
        master=np.array(Image.open(ROOT/'frames/idle.png').convert('RGBA'))
        for name in ['greeting-low','greeting-high']:
            with self.subTest(frame=name):
                frame=np.array(Image.open(ROOT/'frames'/f'{name}.png').convert('RGBA'))
                changed=np.any(frame[833:,755:]!=master[833:,755:],axis=2)
                self.assertEqual(int(changed.sum()),0,f'{name}: rump/tail pixels were redrawn')
    def test_greeting_keeps_all_pixels_below_lower_belly(self):
        master=np.array(Image.open(ROOT/'frames/idle.png').convert('RGBA'))
        for name in ['greeting-low','greeting-high']:
            with self.subTest(frame=name):
                frame=np.array(Image.open(ROOT/'frames'/f'{name}.png').convert('RGBA'))
                self.assertTrue(np.array_equal(frame[920:],master[920:]),f'{name}: leg junction differs')
if __name__=='__main__': unittest.main()
